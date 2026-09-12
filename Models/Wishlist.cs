using System.ComponentModel.DataAnnotations;

namespace KidsWearStore.Models;

public class Wishlist
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}