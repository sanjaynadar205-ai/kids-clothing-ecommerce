using System.ComponentModel.DataAnnotations;

namespace KidsWearStore.Models;

public class ProductImage
{
    public int Id { get; set; }

    [Required]
    [StringLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    [StringLength(200)]
    public string? AltText { get; set; }

    public bool IsPrimary { get; set; }

    public int DisplayOrder { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }
}