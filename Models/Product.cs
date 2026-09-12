using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KidsWearStore.Models;

public class Product
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ShortDescription { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Range(0, 99999999)]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Range(0, 99999999)]
    public decimal? DiscountPrice { get; set; }

    [Range(0, 100)]
    public decimal DiscountPercentage { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    [StringLength(100)]
    public string? Sizes { get; set; }

    [StringLength(1000)]
    public string? Colors { get; set; }

    [StringLength(500)]
    public string? MainImageUrl { get; set; }

    public bool IsFeatured { get; set; }

    public bool IsNewArrival { get; set; }

    public bool IsBestSeller { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Foreign key
    public int CategoryId { get; set; }

    // Navigation property
    public Category? Category { get; set; }

    // A product can contain multiple images.
    public ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();

    // Cart/order relationships
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    // Wishlist relationship
    public ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();
}