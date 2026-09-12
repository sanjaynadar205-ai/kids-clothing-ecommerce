using System.Diagnostics;
using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var featuredProducts = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.IsFeatured)
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync();

        var newArrivals = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.IsNewArrival)
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync();

        var bestSellers = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.IsBestSeller)
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync();

        var offers = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p =>
                p.IsActive &&
                p.DiscountPrice.HasValue &&
                p.DiscountPrice.Value < p.Price)
            .OrderByDescending(p => p.DiscountPercentage)
            .ThenByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync();

        var latestProducts = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync();

        ViewBag.Categories = categories;
        ViewBag.FeaturedProducts = featuredProducts;
        ViewBag.NewArrivals = newArrivals;
        ViewBag.BestSellers = bestSellers;
        ViewBag.Offers = offers;
        ViewBag.LatestProducts = latestProducts;

        return View();
    }

    public IActionResult About()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Terms()
    {
        return View();
    }

    public IActionResult Shipping()
    {
        return View();
    }

    public IActionResult Refund()
    {
        return View();
    }

    public IActionResult Pricing()
    {
        return View();
    }

    public IActionResult Contact()
    {
        return View();
    }

    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error()
    {
        return View(
            new ErrorViewModel
            {
                RequestId =
                    Activity.Current?.Id ??
                    HttpContext.TraceIdentifier
            });
    }
}