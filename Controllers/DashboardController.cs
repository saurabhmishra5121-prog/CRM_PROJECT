using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        bool privileged = User.IsInRole("Admin") || User.IsInRole("Manager");
        var uid = _userManager.GetUserId(User)!;

        var customers = _db.Customers.AsNoTracking().AsQueryable();
        var leads = _db.Leads.AsNoTracking().AsQueryable();
        var opps = _db.Opportunities.AsNoTracking().AsQueryable();

        // Sales Executive sirf apna data dekhega
        if (!privileged)
        {
            customers = customers.Where(c => c.CreatedBy == uid);
            leads = leads.Where(l => l.AssignedTo == uid);
            opps = opps.Where(o => o.AssignedTo == uid);
        }

        // SQLite decimal Sum support nahi karta, isliye memory mein sum karte hain
        var oppList = await opps.Select(o => new { o.Amount, o.Probability, o.Stage, o.Status, o.CreatedDate }).ToListAsync();
        var openOpps = oppList.Where(o => o.Status == "Open").ToList();
        var leadStatuses = await leads.Select(l => l.Status).ToListAsync();

        ViewBag.TotalCustomers = await customers.CountAsync();
        ViewBag.TotalLeads = leadStatuses.Count;
        ViewBag.OpenOpportunities = openOpps.Count;
        ViewBag.OpenPipeline = openOpps.Sum(o => o.Amount);
        ViewBag.WeightedPipeline = openOpps.Sum(o => o.Amount * o.Probability / 100m);
        ViewBag.WonAmount = oppList.Where(o => o.Status == "Won").Sum(o => o.Amount);
        ViewBag.WonCount = oppList.Count(o => o.Status == "Won");
        ViewBag.ConvertedLeads = leadStatuses.Count(s => s == "Converted");

        // Chart 1: Leads by Status
        var leadOrder = new[] { "New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost" };
        ViewBag.LeadLabels = leadOrder;
        ViewBag.LeadCounts = leadOrder.Select(s => leadStatuses.Count(x => x == s)).ToArray();

        // Chart 2: Opportunities by Stage (count)
        var stageOrder = new[] { "Qualification", "Proposal", "Negotiation", "Won", "Lost" };
        ViewBag.StageLabels = stageOrder;
        ViewBag.StageCounts = stageOrder.Select(s => oppList.Count(o => o.Stage == s)).ToArray();

        // Chart 3: Pipeline value by Stage (amount)
        ViewBag.StageAmounts = stageOrder.Select(s => oppList.Where(o => o.Stage == s).Sum(o => o.Amount)).ToArray();

        return View();
    }
}