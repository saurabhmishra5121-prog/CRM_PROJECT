using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private const int PageSize = 10;
    private readonly ApplicationDbContext _db; private readonly AcxiomCRM.Services.IAuditService _audit;
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomersController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, AcxiomCRM.Services.IAuditService audit)
    {
        _db = db;
        _userManager = userManager; _audit = audit;
    }

    private bool IsPrivileged => User.IsInRole("Admin") || User.IsInRole("Manager");
    private string CurrentUserId => _userManager.GetUserId(User)!;

    public async Task<IActionResult> Index(string? search, string? status, int page = 1)
    {
        var uid = CurrentUserId;
        var q = _db.Customers.AsNoTracking().AsQueryable();

        // Sales Executive sirf apne customers dekhega
        if (!IsPrivileged) q = q.Where(c => c.CreatedBy == uid);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.CustomerName.ToLower().Contains(s)
                          || c.Email.ToLower().Contains(s)
                          || c.Phone.Contains(s)
                          || (c.CompanyName != null && c.CompanyName.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(c => c.Status == status);

        var total = await q.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Min(Math.Max(1, page), totalPages);

        var items = await q.OrderByDescending(c => c.CustomerId)
                           .Skip((page - 1) * PageSize)
                           .Take(PageSize)
                           .ToListAsync();

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Total = total;
        return View(items);
    }

    [HttpGet]
    public IActionResult Create() => View(new CustomerViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerViewModel vm)
    {
        await ValidateUniqueAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        var c = new Customer
        {
            CustomerName = vm.CustomerName.Trim(),
            Email = vm.Email.Trim().ToLower(),
            Phone = vm.Phone.Trim(),
            CompanyName = vm.CompanyName?.Trim(),
            Address = vm.Address?.Trim(),
            City = vm.City?.Trim(),
            State = vm.State?.Trim(),
            Status = vm.Status,
            CreatedBy = CurrentUserId,
            CreatedDate = DateTime.UtcNow
        };

        _db.Customers.Add(c);
        await _db.SaveChangesAsync();

        c.CustomerCode = $"CUST-{c.CustomerId:D4}";
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Create Customer", "Customer");
        TempData["Success"] = "Customer created successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var (c, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;
        return View(c);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var (c, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        return View(new CustomerViewModel
        {
            CustomerId = c!.CustomerId,
            CustomerName = c.CustomerName,
            Email = c.Email,
            Phone = c.Phone,
            CompanyName = c.CompanyName,
            Address = c.Address,
            City = c.City,
            State = c.State,
            Status = c.Status
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CustomerViewModel vm)
    {
        if (id != vm.CustomerId) return BadRequest();

        var (c, err) = await GetAuthorizedAsync(id);
        if (err != null) return err;

        await ValidateUniqueAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        c!.CustomerName = vm.CustomerName.Trim();
        c.Email = vm.Email.Trim().ToLower();
        c.Phone = vm.Phone.Trim();
        c.CompanyName = vm.CompanyName?.Trim();
        c.Address = vm.Address?.Trim();
        c.City = vm.City?.Trim();
        c.State = vm.State?.Trim();
        c.Status = vm.Status;
        c.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _audit.LogAsync("Update Customer", "Customer");
        TempData["Success"] = "Customer updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.Customers.FindAsync(id);
        if (c == null) return NotFound();

        if (await _db.Opportunities.AnyAsync(o => o.CustomerId == id))
        {
            TempData["Error"] = "Is customer ke opportunities hain, isliye delete nahi ho sakta. Status Inactive kar do.";
            return RedirectToAction(nameof(Index));
        }

        _db.Customers.Remove(c);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Delete Customer", "Customer");
        TempData["Success"] = "Customer deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<(Customer? customer, IActionResult? error)> GetAuthorizedAsync(int id)
    {
        var c = await _db.Customers.FindAsync(id);
        if (c == null) return (null, NotFound());
        if (!IsPrivileged && c.CreatedBy != CurrentUserId) return (null, Forbid());
        return (c, null);
    }

    // Server-side duplicate check (client-side bypass hone par bhi reject karega)
    private async Task ValidateUniqueAsync(CustomerViewModel vm)
    {
        var email = vm.Email?.Trim().ToLower() ?? string.Empty;
        var phone = vm.Phone?.Trim() ?? string.Empty;

        if (email.Length > 0 &&
            await _db.Customers.AnyAsync(c => c.Email == email && c.CustomerId != vm.CustomerId))
            ModelState.AddModelError(nameof(vm.Email), "A customer with this email already exists.");

        if (phone.Length > 0 &&
            await _db.Customers.AnyAsync(c => c.Phone == phone && c.CustomerId != vm.CustomerId))
            ModelState.AddModelError(nameof(vm.Phone), "A customer with this phone number already exists.");
    }
}
