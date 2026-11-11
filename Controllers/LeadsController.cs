using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class LeadsController : Controller
{
    private const int PageSize = 10;
    private const string SalesRole = "SalesExecutive";

    private static readonly string[] AllStatuses = { "New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost" };
    private static readonly string[] Sources = { "Website", "Referral", "Cold Call", "Email Campaign", "Social Media", "Event", "Other" };

    // Allowed status changes. "Converted" sirf Convert button se hota hai.
    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["New"] = new[] { "Contacted", "Unqualified", "Lost" },
        ["Contacted"] = new[] { "Qualified", "Unqualified", "Lost" },
        ["Qualified"] = new[] { "Contacted", "Lost" },
        ["Unqualified"] = new[] { "Contacted", "Lost" },
        ["Converted"] = Array.Empty<string>(),
        ["Lost"] = Array.Empty<string>()
    };

    private readonly ApplicationDbContext _db; private readonly AcxiomCRM.Services.IAuditService _audit;
    private readonly UserManager<ApplicationUser> _userManager;

    public LeadsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, AcxiomCRM.Services.IAuditService audit)
    {
        _db = db;
        _userManager = userManager; _audit = audit;
    }

    private bool IsPrivileged => User.IsInRole("Admin") || User.IsInRole("Manager");
    private string CurrentUserId => _userManager.GetUserId(User)!;

    private async Task<List<ApplicationUser>> GetSalesUsersAsync()
    {
        var users = await _userManager.GetUsersInRoleAsync(SalesRole);
        return users.Where(u => u.IsActive).OrderBy(u => u.FullName).ToList();
    }

    private static bool CanMove(string from, string to) =>
        Transitions.TryGetValue(from, out var next) && next.Contains(to);

    private async Task PopulateLookupsAsync(string currentStatus, bool isCreate)
    {
        var statusList = new List<string> { isCreate ? "New" : currentStatus };
        if (!isCreate && Transitions.TryGetValue(currentStatus, out var next))
            statusList.AddRange(next);

        ViewBag.StatusOptions = new SelectList(statusList);
        ViewBag.SourceOptions = new SelectList(Sources);
        ViewBag.CanAssign = IsPrivileged;

        var users = await GetSalesUsersAsync();
        var items = new List<SelectListItem> { new("-- Unassigned --", "") };
        items.AddRange(users.Select(u => new SelectListItem($"{u.FullName} ({u.Email})", u.Id)));
        ViewBag.UserOptions = new SelectList(items, "Value", "Text");
    }

    public async Task<IActionResult> Index(string? search, string? status, string? assignedTo, int page = 1)
    {
        var uid = CurrentUserId;
        var q = _db.Leads.AsNoTracking().AsQueryable();

        // Sales Executive sirf apne assigned leads dekhega
        if (!IsPrivileged) q = q.Where(l => l.AssignedTo == uid);
        else if (!string.IsNullOrWhiteSpace(assignedTo)) q = q.Where(l => l.AssignedTo == assignedTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(l => l.LeadName.ToLower().Contains(s)
                          || l.Email.ToLower().Contains(s)
                          || l.Phone.Contains(s)
                          || (l.CompanyName != null && l.CompanyName.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(l => l.Status == status);

        var total = await q.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Min(Math.Max(1, page), totalPages);

        var items = await q.OrderByDescending(l => l.LeadId)
                           .Skip((page - 1) * PageSize)
                           .Take(PageSize)
                           .ToListAsync();

        ViewBag.UserNames = await _userManager.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName);

        var filterItems = new List<SelectListItem>();
        filterItems.AddRange((await GetSalesUsersAsync()).Select(u => new SelectListItem(u.FullName, u.Id)));
        ViewBag.UserFilter = new SelectList(filterItems, "Value", "Text");

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.AssignedTo = assignedTo;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync("New", true);
        return View(new LeadViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeadViewModel vm)
    {
        vm.Status = "New";   // naya lead hamesha New se shuru hota hai
        ModelState.Remove(nameof(vm.Status));
        await ValidateCommonAsync(vm);

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync("New", true);
            return View(vm);
        }

        var lead = new Lead
        {
            LeadName = vm.LeadName.Trim(),
            Email = vm.Email.Trim().ToLower(),
            Phone = vm.Phone.Trim(),
            CompanyName = vm.CompanyName?.Trim(),
            Source = vm.Source,
            Status = "New",
            ExpectedValue = vm.ExpectedValue,
            CreatedDate = DateTime.UtcNow,
            AssignedTo = IsPrivileged ? NullIfEmpty(vm.AssignedTo) : CurrentUserId
        };

        _db.Leads.Add(lead);
        await _db.SaveChangesAsync();

        lead.LeadCode = $"LEAD-{lead.LeadId:D4}";
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Create Lead", "Lead", lead.LeadCode, lead.LeadName);
        TempData["Success"] = "Lead created successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var (lead, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        ViewBag.AssignedName = lead!.AssignedTo == null
            ? null
            : (await _userManager.FindByIdAsync(lead.AssignedTo))?.FullName;
        return View(lead);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var (lead, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        if (lead!.Status == "Converted")
        {
            TempData["Error"] = "Converted lead edit nahi ho sakta.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await PopulateLookupsAsync(lead.Status, false);
        return View(new LeadViewModel
        {
            LeadId = lead.LeadId,
            LeadName = lead.LeadName,
            Email = lead.Email,
            Phone = lead.Phone,
            CompanyName = lead.CompanyName,
            Source = lead.Source,
            Status = lead.Status,
            ExpectedValue = lead.ExpectedValue,
            AssignedTo = lead.AssignedTo
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LeadViewModel vm)
    {
        if (id != vm.LeadId) return BadRequest();

        var (lead, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        if (lead!.Status == "Converted")
        {
            TempData["Error"] = "Converted lead edit nahi ho sakta.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await ValidateCommonAsync(vm);

        // Server-side status workflow check
        if (!AllStatuses.Contains(vm.Status))
            ModelState.AddModelError(nameof(vm.Status), "Invalid lead status.");
        else if (vm.Status != lead.Status)
        {
            if (vm.Status == "Converted")
                ModelState.AddModelError(nameof(vm.Status), "Use the Convert button to convert a Qualified lead.");
            else if (!CanMove(lead.Status, vm.Status))
                ModelState.AddModelError(nameof(vm.Status), $"Invalid status change: {lead.Status} to {vm.Status}.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(lead.Status, false);
            return View(vm);
        }

        lead.LeadName = vm.LeadName.Trim();
        lead.Email = vm.Email.Trim().ToLower();
        lead.Phone = vm.Phone.Trim();
        lead.CompanyName = vm.CompanyName?.Trim();
        lead.Source = vm.Source;
        lead.Status = vm.Status;
        lead.ExpectedValue = vm.ExpectedValue;
        if (IsPrivileged) lead.AssignedTo = NullIfEmpty(vm.AssignedTo);

        await _db.SaveChangesAsync();

        await _audit.LogAsync("Update Lead", "Lead", lead.LeadCode, $"Status: {lead.Status}");
        TempData["Success"] = "Lead updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConvertLead(int id)
    {
        var (lead, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        if (lead!.Status != "Qualified")
        {
            TempData["Error"] = "Sirf Qualified lead convert ho sakta hai.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (lead.ExpectedValue <= 0)
        {
            TempData["Error"] = "Convert karne se pehle Expected Value 0 se zyada set karo (Edit se).";
            return RedirectToAction(nameof(Details), new { id });
        }

        var email = lead.Email.Trim().ToLower();
        var phone = lead.Phone.Trim();

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Same email/phone ka customer pehle se ho toh wahi use karo (duplicate nahi banega)
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email || c.Phone == phone);
        var reused = customer != null;

        if (customer == null)
        {
            customer = new Customer
            {
                CustomerName = lead.LeadName,
                Email = email,
                Phone = phone,
                CompanyName = lead.CompanyName,
                Status = "Active",
                CreatedBy = lead.AssignedTo ?? CurrentUserId,
                CreatedDate = DateTime.UtcNow
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            customer.CustomerCode = $"CUST-{customer.CustomerId:D4}";
            await _db.SaveChangesAsync();
        }

        var opp = new Opportunity
        {
            OpportunityName = $"{lead.LeadName} - Opportunity",
            CustomerId = customer.CustomerId,
            LeadId = lead.LeadId,
            Amount = lead.ExpectedValue,
            Stage = "Qualification",
            Probability = 20,
            ExpectedCloseDate = DateTime.Today.AddDays(30),
            Status = "Open",
            CreatedDate = DateTime.UtcNow,
            AssignedTo = lead.AssignedTo ?? CurrentUserId
        };
        _db.Opportunities.Add(opp);

        lead.Status = "Converted";
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _audit.LogAsync("Convert Lead", "Lead", lead.LeadCode, $"Opportunity #{opp.OpportunityId}");
        TempData["Success"] = reused
            ? "Lead converted. Existing customer use hua aur naya opportunity ban gaya."
            : "Lead converted: naya Customer aur Opportunity ban gaya.";
        return RedirectToAction("Details", "Opportunities", new { id = opp.OpportunityId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var lead = await _db.Leads.FindAsync(id);
        if (lead == null) return NotFound();

        if (lead.Status == "Converted")
        {
            TempData["Error"] = "Converted lead delete nahi ho sakta.";
            return RedirectToAction(nameof(Index));
        }

        _db.Leads.Remove(lead);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Delete Lead", "Lead", id.ToString(), lead.LeadName);
        TempData["Success"] = "Lead deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<(Lead? lead, IActionResult? error)> GetAuthorizedAsync(int id)
    {
        var lead = await _db.Leads.FindAsync(id);
        if (lead == null) return (null, NotFound());
        if (!IsPrivileged && lead.AssignedTo != CurrentUserId) return (null, Forbid());
        return (lead, null);
    }

    private async Task ValidateCommonAsync(LeadViewModel vm)
    {
        if (!Sources.Contains(vm.Source))
            ModelState.AddModelError(nameof(vm.Source), "Select a valid lead source.");

        if (IsPrivileged && !string.IsNullOrEmpty(vm.AssignedTo))
        {
            var users = await GetSalesUsersAsync();
            if (!users.Any(u => u.Id == vm.AssignedTo))
                ModelState.AddModelError(nameof(vm.AssignedTo), "Select a valid Sales Executive.");
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
