using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class OpportunitiesController : Controller
{
    private const int PageSize = 10;
    private const string SalesRole = "SalesExecutive";
    private static readonly string[] Stages = { "Qualification", "Proposal", "Negotiation", "Won", "Lost" };

    private static bool IsActiveStage(string stage) => stage != "Won" && stage != "Lost";
    private static string StatusFor(string stage) => stage == "Won" ? "Won" : stage == "Lost" ? "Lost" : "Open";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public OpportunitiesController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    private bool IsPrivileged => User.IsInRole("Admin") || User.IsInRole("Manager");
    private string CurrentUserId => _userManager.GetUserId(User)!;

    private async Task<List<ApplicationUser>> GetSalesUsersAsync()
    {
        var users = await _userManager.GetUsersInRoleAsync(SalesRole);
        return users.Where(u => u.IsActive).OrderBy(u => u.FullName).ToList();
    }

    private async Task PopulateLookupsAsync(int? includeCustomerId)
    {
        var uid = CurrentUserId;
        var cq = _db.Customers.AsNoTracking().Where(c => c.Status == "Active" || c.CustomerId == includeCustomerId);
        if (!IsPrivileged)
            cq = cq.Where(c => c.CreatedBy == uid || c.CustomerId == includeCustomerId);

        var customers = await cq.OrderBy(c => c.CustomerName).ToListAsync();
        var custItems = new List<SelectListItem> { new("-- Select Customer --", "0") };
        custItems.AddRange(customers.Select(c => new SelectListItem($"{c.CustomerCode} - {c.CustomerName}", c.CustomerId.ToString())));
        ViewBag.CustomerOptions = new SelectList(custItems, "Value", "Text");

        ViewBag.StageOptions = new SelectList(Stages);
        ViewBag.CanAssign = IsPrivileged;

        var users = await GetSalesUsersAsync();
        var userItems = new List<SelectListItem> { new("-- Myself --", "") };
        userItems.AddRange(users.Select(u => new SelectListItem($"{u.FullName} ({u.Email})", u.Id)));
        ViewBag.UserOptions = new SelectList(userItems, "Value", "Text");
    }

    public async Task<IActionResult> Index(string? search, string? stage, string? status, int page = 1)
    {
        var uid = CurrentUserId;
        var q = _db.Opportunities.Include(o => o.Customer).AsNoTracking().AsQueryable();

        // Sales Executive sirf apne opportunities dekhega
        if (!IsPrivileged) q = q.Where(o => o.AssignedTo == uid);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(o => o.OpportunityName.ToLower().Contains(s)
                          || (o.Customer != null && o.Customer.CustomerName.ToLower().Contains(s)));
        }
        if (!string.IsNullOrWhiteSpace(stage)) q = q.Where(o => o.Stage == stage);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);

        var total = await q.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Min(Math.Max(1, page), totalPages);

        // Sales Pipeline summary (open opportunities, in-memory sum kyunki SQLite decimal Sum support nahi karta)
        var open = await q.Where(o => o.Status == "Open")
                          .Select(o => new { o.Amount, o.Probability })
                          .ToListAsync();
        ViewBag.OpenPipeline = open.Sum(x => x.Amount);
        ViewBag.Weighted = open.Sum(x => x.Amount * x.Probability / 100m);

        var items = await q.OrderByDescending(o => o.OpportunityId)
                           .Skip((page - 1) * PageSize)
                           .Take(PageSize)
                           .ToListAsync();

        ViewBag.UserNames = await _userManager.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName);
        ViewBag.Search = search;
        ViewBag.Stage = stage;
        ViewBag.Status = status;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync(null);
        return View(new OpportunityViewModel { Probability = 10 });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OpportunityViewModel vm)
    {
        await ValidateRulesAsync(vm, null);
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(vm.CustomerId);
            return View(vm);
        }

        var opp = new Opportunity
        {
            OpportunityName = vm.OpportunityName.Trim(),
            CustomerId = vm.CustomerId,
            Amount = vm.Amount,
            Stage = vm.Stage,
            Probability = vm.Probability,
            ExpectedCloseDate = vm.ExpectedCloseDate.Date,
            Status = StatusFor(vm.Stage),
            CreatedDate = DateTime.UtcNow,
            AssignedTo = ResolveOwner(vm.AssignedTo, null)
        };

        _db.Opportunities.Add(opp);
        await _db.SaveChangesAsync();

        // Phase 4: audit log "Create Opportunity"
        TempData["Success"] = "Opportunity created successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var (opp, err) = await GetAuthorizedAsync(id, includeCustomer: true);
        if (err != null) return err;

        ViewBag.OwnerName = opp!.AssignedTo == null
            ? null
            : (await _userManager.FindByIdAsync(opp.AssignedTo))?.FullName;
        return View(opp);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var (opp, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        await PopulateLookupsAsync(opp!.CustomerId);
        return View(new OpportunityViewModel
        {
            OpportunityId = opp.OpportunityId,
            OpportunityName = opp.OpportunityName,
            CustomerId = opp.CustomerId,
            Amount = opp.Amount,
            Stage = opp.Stage,
            Probability = opp.Probability,
            ExpectedCloseDate = opp.ExpectedCloseDate,
            AssignedTo = ""
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OpportunityViewModel vm)
    {
        if (id != vm.OpportunityId) return BadRequest();

        var (opp, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        await ValidateRulesAsync(vm, opp!.CustomerId);
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(opp.CustomerId);
            return View(vm);
        }

        opp.OpportunityName = vm.OpportunityName.Trim();
        opp.CustomerId = vm.CustomerId;
        opp.Amount = vm.Amount;
        opp.Stage = vm.Stage;
        opp.Probability = vm.Probability;
        opp.ExpectedCloseDate = vm.ExpectedCloseDate.Date;
        opp.Status = StatusFor(vm.Stage);
        opp.AssignedTo = ResolveOwner(vm.AssignedTo, opp.AssignedTo);

        await _db.SaveChangesAsync();

        // Phase 4: audit log "Update Opportunity / stage change"
        TempData["Success"] = "Opportunity updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var opp = await _db.Opportunities.FindAsync(id);
        if (opp == null) return NotFound();

        _db.Opportunities.Remove(opp);
        await _db.SaveChangesAsync();

        // Phase 4: audit log "Delete Opportunity"
        TempData["Success"] = "Opportunity deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<(Opportunity? opp, IActionResult? error)> GetAuthorizedAsync(int id, bool includeCustomer = false)
    {
        var q = _db.Opportunities.AsQueryable();
        if (includeCustomer) q = q.Include(o => o.Customer);

        var opp = await q.FirstOrDefaultAsync(o => o.OpportunityId == id);
        if (opp == null) return (null, NotFound());
        if (!IsPrivileged && opp.AssignedTo != CurrentUserId) return (null, Forbid());
        return (opp, null);
    }

    // Amount > 0 aur Probability 0-100 DataAnnotations se server par bhi enforce hote hain.
    // Yahan baaki business rules hain.
    private async Task ValidateRulesAsync(OpportunityViewModel vm, int? currentCustomerId)
    {
        if (!Stages.Contains(vm.Stage))
            ModelState.AddModelError(nameof(vm.Stage), "Select a valid stage.");
        else if (IsActiveStage(vm.Stage) && vm.ExpectedCloseDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(vm.ExpectedCloseDate), "Expected Close Date cannot be in the past.");

        if (ModelState.GetFieldValidationState(nameof(vm.CustomerId)) != ModelValidationState.Invalid)
        {
            var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == vm.CustomerId);
            if (customer == null)
                ModelState.AddModelError(nameof(vm.CustomerId), "Selected customer does not exist.");
            else if (!IsPrivileged && customer.CustomerId != currentCustomerId && customer.CreatedBy != CurrentUserId)
                ModelState.AddModelError(nameof(vm.CustomerId), "You can only use your own customers.");
        }

        if (IsPrivileged && !string.IsNullOrEmpty(vm.AssignedTo))
        {
            var users = await GetSalesUsersAsync();
            if (!users.Any(u => u.Id == vm.AssignedTo))
                ModelState.AddModelError(nameof(vm.AssignedTo), "Select a valid Sales Executive.");
        }
    }

    private string ResolveOwner(string? requested, string? existing)
    {
        if (!IsPrivileged) return existing ?? CurrentUserId;
        return string.IsNullOrWhiteSpace(requested) ? (existing ?? CurrentUserId) : requested;
    }
}