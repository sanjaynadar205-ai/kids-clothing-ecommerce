using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

public class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public ProductsController(
        ApplicationDbContext context,
        IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    // =========================================================
    // SHOP
    // =========================================================

    public async Task<IActionResult> Index(
        string? search,
        int? categoryId,
        decimal? minPrice,
        decimal? maxPrice,
        string? sort,
        string? availability)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .AsQueryable();

        // SEARCH
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(p =>
                p.Name.Contains(search) ||
                (p.ShortDescription != null &&
                 p.ShortDescription.Contains(search)) ||
                (p.Description != null &&
                 p.Description.Contains(search)) ||
                (p.Category != null &&
                 p.Category.Name.Contains(search)));
        }

        // CATEGORY
        if (categoryId.HasValue)
        {
            query = query.Where(
                p => p.CategoryId == categoryId.Value);
        }

        // MINIMUM PRICE
        if (minPrice.HasValue)
        {
            query = query.Where(p =>
                (p.DiscountPrice ?? p.Price) >= minPrice.Value);
        }

        // MAXIMUM PRICE
        if (maxPrice.HasValue)
        {
            query = query.Where(p =>
                (p.DiscountPrice ?? p.Price) <= maxPrice.Value);
        }

        // AVAILABILITY
        if (!string.IsNullOrWhiteSpace(availability))
        {
            if (availability.Equals(
                    "in-stock",
                    StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p =>
                    p.StockQuantity > 0);
            }
            else if (availability.Equals(
                         "out-of-stock",
                         StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p =>
                    p.StockQuantity <= 0);
            }
        }

        // SORTING
        query = sort?.ToLowerInvariant() switch
        {
            "price-low" =>
                query.OrderBy(p =>
                    p.DiscountPrice ?? p.Price),

            "price-high" =>
                query.OrderByDescending(p =>
                    p.DiscountPrice ?? p.Price),

            "name-az" =>
                query.OrderBy(p => p.Name),

            "name-za" =>
                query.OrderByDescending(p => p.Name),

            "oldest" =>
                query.OrderBy(p => p.CreatedAt),

            "newest" =>
                query.OrderByDescending(p => p.CreatedAt),

            "featured" =>
                query
                    .OrderByDescending(p => p.IsFeatured)
                    .ThenByDescending(p => p.CreatedAt),

            "bestseller" =>
                query
                    .OrderByDescending(p => p.IsBestSeller)
                    .ThenByDescending(p => p.CreatedAt),

            "discount" =>
                query
                    .OrderByDescending(p => p.DiscountPercentage)
                    .ThenByDescending(p => p.CreatedAt),

            _ =>
                query
                    .OrderByDescending(p => p.IsFeatured)
                    .ThenByDescending(p => p.CreatedAt)
        };

        var products = await query.ToListAsync();

        // Preserve filter values
        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.MinPrice = minPrice;
        ViewBag.MaxPrice = maxPrice;
        ViewBag.Sort = sort;
        ViewBag.Availability = availability;

        // Categories
        ViewBag.Categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Shop statistics
        ViewBag.TotalProducts = await _context.Products
            .CountAsync(p => p.IsActive);

        ViewBag.InStockProducts = await _context.Products
            .CountAsync(p =>
                p.IsActive &&
                p.StockQuantity > 0);

        ViewBag.FeaturedProducts = await _context.Products
            .CountAsync(p =>
                p.IsActive &&
                p.IsFeatured);

        return View(products);
    }


    // =========================================================
    // PRODUCT DETAILS
    // =========================================================

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductImages)
            .FirstOrDefaultAsync(
                p => p.Id == id &&
                     p.IsActive);

        if (product == null)
        {
            return NotFound();
        }

        return View(product);
    }


    // =========================================================
    // MANAGE PRODUCTS
    // =========================================================

    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Manage()
    {
        var products = await _context.Products
            .Include(p => p.Category)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return View(products);
    }


    // =========================================================
    // CREATE PRODUCT - GET
    // =========================================================

    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Create()
    {
        await LoadCategories();

        return View();
    }


    // =========================================================
    // CREATE PRODUCT - POST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Create(
        Product product,
        IFormFile? imageFile)
    {
        // Validate category
        var categoryExists = await _context.Categories
            .AnyAsync(c =>
                c.Id == product.CategoryId &&
                c.IsActive);

        if (!categoryExists)
        {
            ModelState.AddModelError(
                "CategoryId",
                "Please select a valid category.");
        }

        // Validate image
        if (imageFile != null &&
            imageFile.Length > 0)
        {
            if (!IsValidImage(imageFile))
            {
                ModelState.AddModelError(
                    "imageFile",
                    "Only JPG, JPEG, PNG and WEBP images are allowed.");
            }

            if (imageFile.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(
                    "imageFile",
                    "Image size cannot exceed 5 MB.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadCategories();
            return View(product);
        }

        // Upload image
        if (imageFile != null &&
            imageFile.Length > 0)
        {
            product.MainImageUrl =
                await SaveImage(imageFile);
        }

        product.CreatedAt = DateTime.UtcNow;
        product.UpdatedAt = null;

        _context.Products.Add(product);

        await _context.SaveChangesAsync();

        TempData["Success"] =
            "Product added successfully.";

        return RedirectToAction(nameof(Manage));
    }


    // =========================================================
    // EDIT PRODUCT - GET
    // =========================================================

    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var product = await _context.Products
            .FirstOrDefaultAsync(
                p => p.Id == id);

        if (product == null)
        {
            return NotFound();
        }

        await LoadCategories();

        return View(product);
    }


    // =========================================================
    // EDIT PRODUCT - POST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Edit(
        int id,
        Product product,
        IFormFile? imageFile)
    {
        if (id != product.Id)
        {
            return NotFound();
        }

        // Validate category
        var categoryExists = await _context.Categories
            .AnyAsync(c =>
                c.Id == product.CategoryId &&
                c.IsActive);

        if (!categoryExists)
        {
            ModelState.AddModelError(
                "CategoryId",
                "Please select a valid category.");
        }

        // Validate new image
        if (imageFile != null &&
            imageFile.Length > 0)
        {
            if (!IsValidImage(imageFile))
            {
                ModelState.AddModelError(
                    "imageFile",
                    "Only JPG, JPEG, PNG and WEBP images are allowed.");
            }

            if (imageFile.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(
                    "imageFile",
                    "Image size cannot exceed 5 MB.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadCategories();
            return View(product);
        }

        var existingProduct =
            await _context.Products
                .FirstOrDefaultAsync(
                    p => p.Id == id);

        if (existingProduct == null)
        {
            return NotFound();
        }

        // Save old image path
        var oldImage =
            existingProduct.MainImageUrl;

        // Update product fields
        existingProduct.Name =
            product.Name;

        existingProduct.ShortDescription =
            product.ShortDescription;

        existingProduct.Description =
            product.Description;

        existingProduct.Price =
            product.Price;

        existingProduct.DiscountPrice =
            product.DiscountPrice;

        existingProduct.DiscountPercentage =
            product.DiscountPercentage;

        existingProduct.StockQuantity =
            product.StockQuantity;

        existingProduct.Sizes =
            product.Sizes;

        existingProduct.Colors =
            product.Colors;

        existingProduct.IsFeatured =
            product.IsFeatured;

        existingProduct.IsNewArrival =
            product.IsNewArrival;

        existingProduct.IsBestSeller =
            product.IsBestSeller;

        existingProduct.IsActive =
            product.IsActive;

        existingProduct.CategoryId =
            product.CategoryId;

        // Replace image only when new image selected
        if (imageFile != null &&
            imageFile.Length > 0)
        {
            existingProduct.MainImageUrl =
                await SaveImage(imageFile);

            DeleteImage(oldImage);
        }

        existingProduct.UpdatedAt =
            DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Success"] =
            "Product updated successfully.";

        return RedirectToAction(nameof(Manage));
    }


    // =========================================================
    // DEACTIVATE PRODUCT
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var product =
            await _context.Products
                .FirstOrDefaultAsync(
                    p => p.Id == id);

        if (product == null)
        {
            return NotFound();
        }

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Success"] =
            "Product deactivated successfully.";

        return RedirectToAction(nameof(Manage));
    }


    // =========================================================
    // ACTIVATE PRODUCT
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Activate(int id)
    {
        var product =
            await _context.Products
                .FirstOrDefaultAsync(
                    p => p.Id == id);

        if (product == null)
        {
            return NotFound();
        }

        product.IsActive = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Success"] =
            "Product activated successfully.";

        return RedirectToAction(nameof(Manage));
    }


    // =========================================================
    // PERMANENT DELETE PRODUCT
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "ProductManager,Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _context.Products
            .Include(p => p.ProductImages)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
        {
            return NotFound();
        }

        // Do not permanently delete a product that is part
        // of an existing order. Historical orders must remain
        // intact.
        var hasOrderItems = await _context.OrderItems
            .AnyAsync(oi => oi.ProductId == id);

        if (hasOrderItems)
        {
            TempData["Error"] =
                "This product cannot be permanently deleted because it is associated with an existing order. Deactivate it instead.";

            return RedirectToAction(nameof(Manage));
        }

        // Remove cart references first because Product -> CartItem
        // uses a restricted relationship.
        var cartItems = await _context.CartItems
            .Where(ci => ci.ProductId == id)
            .ToListAsync();

        if (cartItems.Count > 0)
        {
            _context.CartItems.RemoveRange(cartItems);
        }

        // Remove wishlist references.
        var wishlists = await _context.Wishlists
            .Where(w => w.ProductId == id)
            .ToListAsync();

        if (wishlists.Count > 0)
        {
            _context.Wishlists.RemoveRange(wishlists);
        }

        // Delete the main uploaded image.
        DeleteImage(product.MainImageUrl);

        // Delete additional uploaded images.
        foreach (var productImage in product.ProductImages)
        {
            DeleteImage(productImage.ImageUrl);
        }

        // ProductImages are cascade deleted by the database.
        _context.Products.Remove(product);

        await _context.SaveChangesAsync();

        TempData["Success"] =
            "Product permanently deleted.";

        return RedirectToAction(nameof(Manage));
    }


    // =========================================================
    // LOAD CATEGORIES
    // =========================================================

    private async Task LoadCategories()
    {
        var categories =
            await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();

        ViewBag.Categories =
            new SelectList(
                categories,
                "Id",
                "Name");
    }


    // =========================================================
    // IMAGE VALIDATION
    // =========================================================

    private bool IsValidImage(IFormFile file)
    {
        var allowedExtensions =
            new[]
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

        var extension =
            Path.GetExtension(file.FileName)
                .ToLowerInvariant();

        return allowedExtensions.Contains(extension);
    }


    // =========================================================
    // SAVE IMAGE
    // =========================================================

    private async Task<string> SaveImage(
        IFormFile imageFile)
    {
        var uploadsFolder =
            Path.Combine(
                _environment.WebRootPath,
                "uploads",
                "products");

        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(
                uploadsFolder);
        }

        var extension =
            Path.GetExtension(
                imageFile.FileName)
                .ToLowerInvariant();

        var fileName =
            $"{Guid.NewGuid():N}{extension}";

        var filePath =
            Path.Combine(
                uploadsFolder,
                fileName);

        using (var stream =
               new FileStream(
                   filePath,
                   FileMode.Create))
        {
            await imageFile.CopyToAsync(
                stream);
        }

        return
            $"/uploads/products/{fileName}";
    }


    // =========================================================
    // DELETE IMAGE FILE
    // =========================================================

    private void DeleteImage(
        string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        if (!imageUrl.StartsWith(
                "/uploads/products/",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName =
            Path.GetFileName(imageUrl);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var filePath =
            Path.Combine(
                _environment.WebRootPath,
                "uploads",
                "products",
                fileName);

        if (System.IO.File.Exists(filePath))
        {
            try
            {
                System.IO.File.Delete(filePath);
            }
            catch
            {
                // Do not prevent database operations if
                // an old image file cannot be removed.
            }
        }
    }
}