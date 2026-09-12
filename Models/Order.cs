using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KidsWearStore.Models;

public class Order
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string OrderNumber { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required]
    [StringLength(150)]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(200)]
    public string CustomerEmail { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(30)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string ShippingAddress { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string State { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string PostalCode { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Required]
    [StringLength(50)]
    public string PaymentMethod { get; set; } = "Cash on Delivery";

    // ----------------------------------------------------
    // RAZORPAY PAYMENT INFORMATION
    // ----------------------------------------------------

    [StringLength(100)]
    public string? RazorpayOrderId { get; set; }

    [StringLength(100)]
    public string? RazorpayPaymentId { get; set; }

    [StringLength(500)]
    public string? RazorpaySignature { get; set; }

    [StringLength(50)]
    public string PaymentStatus { get; set; } = "Pending";

    // ----------------------------------------------------
    // ORDER STATUS
    // ----------------------------------------------------

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Records who cancelled the order.
    // null      = order has not been cancelled
    // Customer  = cancelled by customer
    // Admin     = cancelled by admin
    [StringLength(20)]
    public string? CancelledBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<OrderItem> OrderItems { get; set; }
        = new List<OrderItem>();
}