using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

[Authorize(Roles = "Admin")]
public class AdminDashboardController : Controller
{
    private readonly ApplicationDbContext _context;

    public AdminDashboardController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var totalOrders = await _context.Orders.CountAsync();

        var totalSales = await _context.Orders
            .Where(o =>
                o.Status != OrderStatus.Cancelled &&
                o.PaymentStatus != "Failed")
            .Select(o => (decimal?)o.TotalAmount)
            .SumAsync() ?? 0m;

        var pendingOrders = await _context.Orders
            .CountAsync(o =>
                o.Status == OrderStatus.Pending);

        var confirmedOrders = await _context.Orders
            .CountAsync(o =>
                o.Status == OrderStatus.Confirmed);

        var processingOrders = await _context.Orders
            .CountAsync(o =>
                o.Status == OrderStatus.Processing);

        var shippedOrders = await _context.Orders
            .CountAsync(o =>
                o.Status == OrderStatus.Shipped);

        var deliveredOrders = await _context.Orders
            .CountAsync(o =>
                o.Status == OrderStatus.Delivered);

        var cancelledOrders = await _context.Orders
            .CountAsync(o =>
                o.Status == OrderStatus.Cancelled);

        var totalCustomers = await _context.Users.CountAsync();

        var totalProducts = await _context.Products.CountAsync();

        var activeProducts = await _context.Products
            .CountAsync(p => p.IsActive);

        var lowStockProducts = await _context.Products
            .CountAsync(p =>
                p.IsActive &&
                p.StockQuantity > 0 &&
                p.StockQuantity <= 5);

        var outOfStockProducts = await _context.Products
            .CountAsync(p =>
                p.IsActive &&
                p.StockQuantity == 0);

        var recentOrders = await _context.Orders
            .Include(o => o.User)
            .OrderByDescending(o => o.CreatedAt)
            .Take(8)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var startOfMonth = new DateTime(
            today.Year,
            today.Month,
            1);

        var monthlySales = await _context.Orders
            .Where(o =>
                o.CreatedAt >= startOfMonth &&
                o.Status != OrderStatus.Cancelled &&
                o.PaymentStatus != "Failed")
            .Select(o => (decimal?)o.TotalAmount)
            .SumAsync() ?? 0m;

        var todaySales = await _context.Orders
            .Where(o =>
                o.CreatedAt >= today &&
                o.Status != OrderStatus.Cancelled &&
                o.PaymentStatus != "Failed")
            .Select(o => (decimal?)o.TotalAmount)
            .SumAsync() ?? 0m;

        var dashboard = new AdminDashboardViewModel
        {
            TotalOrders = totalOrders,
            TotalSales = totalSales,
            PendingOrders = pendingOrders,
            ConfirmedOrders = confirmedOrders,
            ProcessingOrders = processingOrders,
            ShippedOrders = shippedOrders,
            DeliveredOrders = deliveredOrders,
            CancelledOrders = cancelledOrders,

            TotalCustomers = totalCustomers,

            TotalProducts = totalProducts,
            ActiveProducts = activeProducts,
            LowStockProducts = lowStockProducts,
            OutOfStockProducts = outOfStockProducts,

            MonthlySales = monthlySales,
            TodaySales = todaySales,

            RecentOrders = recentOrders
        };

        return View(dashboard);
    }
}

public class AdminDashboardViewModel
{
    public int TotalOrders { get; set; }

    public decimal TotalSales { get; set; }

    public int PendingOrders { get; set; }

    public int ConfirmedOrders { get; set; }

    public int ProcessingOrders { get; set; }

    public int ShippedOrders { get; set; }

    public int DeliveredOrders { get; set; }

    public int CancelledOrders { get; set; }

    public int TotalCustomers { get; set; }

    public int TotalProducts { get; set; }

    public int ActiveProducts { get; set; }

    public int LowStockProducts { get; set; }

    public int OutOfStockProducts { get; set; }

    public decimal MonthlySales { get; set; }

    public decimal TodaySales { get; set; }

    public List<Order> RecentOrders { get; set; } = new();
}