using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

[Authorize(Roles = "Admin")]
public class AdminOrdersController : Controller
{
    private readonly ApplicationDbContext _context;

    public AdminOrdersController(ApplicationDbContext context)
    {
        _context = context;
    }

    // =========================================================
    // ORDER LIST
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        OrderStatus? status,
        string? paymentStatus)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(o =>
                o.OrderNumber.Contains(search) ||
                o.CustomerName.Contains(search) ||
                o.CustomerEmail.Contains(search) ||
                o.CustomerPhone.Contains(search));
        }

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(paymentStatus))
        {
            paymentStatus = paymentStatus.Trim();

            query = query.Where(o =>
                o.PaymentStatus == paymentStatus);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var model = new AdminOrdersViewModel
        {
            Orders = orders,
            Search = search,
            Status = status,
            PaymentStatus = paymentStatus,

            TotalOrders = await _context.Orders.CountAsync(),

            PendingOrders = await _context.Orders
                .CountAsync(o => o.Status == OrderStatus.Pending),

            ConfirmedOrders = await _context.Orders
                .CountAsync(o => o.Status == OrderStatus.Confirmed),

            ProcessingOrders = await _context.Orders
                .CountAsync(o => o.Status == OrderStatus.Processing),

            ShippedOrders = await _context.Orders
                .CountAsync(o => o.Status == OrderStatus.Shipped),

            DeliveredOrders = await _context.Orders
                .CountAsync(o => o.Status == OrderStatus.Delivered),

            CancelledOrders = await _context.Orders
                .CountAsync(o => o.Status == OrderStatus.Cancelled)
        };

        return View(model);
    }


    // =========================================================
    // ORDER DETAILS
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.Orders
            .Include(o => o.User)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }


    // =========================================================
    // UPDATE ORDER STATUS
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(
        int id,
        OrderStatus status)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            return NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            TempData["Error"] =
                "A cancelled order cannot be reopened.";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        if (order.Status == OrderStatus.Delivered)
        {
            TempData["Error"] =
                "A delivered order cannot be changed.";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        if (status == OrderStatus.Cancelled)
        {
            TempData["Error"] =
                "Use the cancellation action to cancel an order.";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        if (!IsValidForwardStatus(order.Status, status))
        {
            TempData["Error"] =
                $"Invalid status change from {order.Status} to {status}.";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        order.Status = status;
        order.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Success"] =
            $"Order #{order.OrderNumber} is now {GetStatusLabel(status)}.";

        return RedirectToAction(
            nameof(Details),
            new { id });
    }


    // =========================================================
    // CANCEL ORDER
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            return NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            TempData["Error"] =
                "This order is already cancelled.";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        if (order.Status == OrderStatus.Delivered)
        {
            TempData["Error"] =
                "A delivered order cannot be cancelled.";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        // -----------------------------------------------------
        // Restore stock.
        // -----------------------------------------------------

        foreach (var item in order.OrderItems)
        {
            if (item.Product != null)
            {
                item.Product.StockQuantity += item.Quantity;
                item.Product.UpdatedAt = DateTime.UtcNow;
            }
        }

        // -----------------------------------------------------
        // Cancel order.
        // -----------------------------------------------------

        order.Status = OrderStatus.Cancelled;
        order.CancelledBy = "Admin";
        order.UpdatedAt = DateTime.UtcNow;

        // -----------------------------------------------------
        // Payment-aware cancellation.
        //
        // Paid orders require an actual refund through the
        // payment provider. We do not pretend the refund
        // happened automatically.
        // -----------------------------------------------------

        if (string.Equals(
                order.PaymentStatus,
                "Paid",
                StringComparison.OrdinalIgnoreCase))
        {
            order.PaymentStatus = "Refund Pending";
        }
        else if (string.Equals(
                     order.PaymentStatus,
                     "Created",
                     StringComparison.OrdinalIgnoreCase))
        {
            order.PaymentStatus = "Cancelled";
        }
        else if (string.Equals(
                     order.PaymentStatus,
                     "Pending",
                     StringComparison.OrdinalIgnoreCase))
        {
            order.PaymentStatus = "Cancelled";
        }

        await _context.SaveChangesAsync();

        TempData["Success"] =
            $"Order #{order.OrderNumber} has been cancelled by Admin.";

        return RedirectToAction(
            nameof(Details),
            new { id });
    }


    // =========================================================
    // STATUS HELPERS
    // =========================================================

    private static bool IsValidForwardStatus(
        OrderStatus current,
        OrderStatus requested)
    {
        return current switch
        {
            OrderStatus.Pending =>
                requested == OrderStatus.Confirmed,

            OrderStatus.Confirmed =>
                requested == OrderStatus.Processing,

            OrderStatus.Processing =>
                requested == OrderStatus.Shipped,

            OrderStatus.Shipped =>
                requested == OrderStatus.Delivered,

            _ => false
        };
    }

    private static string GetStatusLabel(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Pending => "Pending",
            OrderStatus.Confirmed => "Confirmed",
            OrderStatus.Processing => "Processing",
            OrderStatus.Shipped => "Shipped",
            OrderStatus.Delivered => "Delivered",
            OrderStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };
    }
}


// =============================================================
// ADMIN ORDERS VIEW MODEL
// =============================================================

public class AdminOrdersViewModel
{
    public List<Order> Orders { get; set; } = new();

    public string? Search { get; set; }

    public OrderStatus? Status { get; set; }

    public string? PaymentStatus { get; set; }

    public int TotalOrders { get; set; }

    public int PendingOrders { get; set; }

    public int ConfirmedOrders { get; set; }

    public int ProcessingOrders { get; set; }

    public int ShippedOrders { get; set; }

    public int DeliveredOrders { get; set; }

    public int CancelledOrders { get; set; }
}