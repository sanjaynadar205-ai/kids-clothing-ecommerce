using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
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
    private readonly Cloudinary _cloudinary;

    public ProductsController(
        ApplicationDbContext context,
        IConfiguration configuration)
    {
        _context = context;

        var cloudName =
            configuration["Cloudinary:CloudName"];

        var apiKey =
            configuration["Cloudinary:ApiKey"];

        var apiSecret =
            configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrWhiteSpace(cloudName) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException(
                "Cloudinary configuration is missing. " +
                "Configure Cloudinary:CloudName, Cloudinary:ApiKey and Cloudinary:ApiSecret.");
        }

        var account =
            new Account(
                cloudName,
                apiKey,
                apiSecret);

        _cloudinary =
            new Cloudinary(account);
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

        if (categoryId.HasValue)
        {
            query = query.Where(
                p => p.CategoryId == categoryId.Value);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p =>
                (p.DiscountPrice ?? p.Price) >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p =>
                (p.DiscountPrice ?? p.Price) <= maxPrice.Value);
        }

        if (!string.IsNullOrWhiteSpace(availability))
        {
            if (availability.Equals(
                    "in-stock",
                    StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(
                    p => p.StockQuantity > 0);
            }
            else if (availability.Equals(
                         "out-of-stock",
                         StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(
                    p => p.StockQuantity <= 0);
            }
        }

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

        var products =
            await query.ToListAsync();

        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.MinPrice = minPrice;
        ViewBag.MaxPrice = maxPrice;
        ViewBag.Sort = sort;
        ViewBag.Availability = availability;

        ViewBag.Categories =
            await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();

        ViewBag.TotalProducts =
            await _context.Products
                .CountAsync(p => p.IsActive);

        ViewBag.InStockProducts =
            await _context.Products
                .CountAsync(p =>
                    p.IsActive &&
                    p.StockQuantity > 0);

        ViewBag.FeaturedProducts =
            await _context.Products
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

        var product =
            await _context.Products
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
        var products =
            await _context.Products
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
        var categoryExists =
            await _context.Categories
                .AnyAsync(c =>
                    c.Id == product.CategoryId &&
                    c.IsActive);

        if (!categoryExists)
        {
            ModelState.AddModelError(
                "CategoryId",
                "Please select a valid category.");
        }

        if (imageFile != null &&
            imageFile.Length > 0)
        {
            if (!IsValidImage(imageFile))
            {
                ModelState.AddModelError(
                    "imageFile",
                    "Only JPG, JPEG, PNG and WEBP images are allowed.");
            }

            if (imageFile.Length >
                5 * 1024 * 1024)
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

        if (imageFile != null &&
            imageFile.Length > 0)
        {
            product.MainImageUrl =
                await UploadImage(imageFile);
        }

        product.CreatedAt =
            DateTime.UtcNow;

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

        var product =
            await _context.Products
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

        var categoryExists =
            await _context.Categories
                .AnyAsync(c =>
                    c.Id == product.CategoryId &&
                    c.IsActive);

        if (!categoryExists)
        {
            ModelState.AddModelError(
                "CategoryId",
                "Please select a valid category.");
        }

        if (imageFile != null &&
            imageFile.Length > 0)
        {
            if (!IsValidImage(imageFile))
            {
                ModelState.AddModelError(
                    "imageFile",
                    "Only JPG, JPEG, PNG and WEBP images are allowed.");
            }

            if (imageFile.Length >
                5 * 1024 * 1024)
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

        var oldImage =
            existingProduct.MainImageUrl;

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

        if (imageFile != null &&
            imageFile.Length > 0)
        {
            existingProduct.MainImageUrl =
                await UploadImage(imageFile);

            await DeleteCloudinaryImage(oldImage);
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
        product.UpdatedAt =
            DateTime.UtcNow;

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
        product.UpdatedAt =
            DateTime.UtcNow;

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
        var product =
            await _context.Products
                .Include(p => p.ProductImages)
                .FirstOrDefaultAsync(
                    p => p.Id == id);

        if (product == null)
        {
            return NotFound();
        }

        var hasOrderItems =
            await _context.OrderItems
                .AnyAsync(
                    oi => oi.ProductId == id);

        if (hasOrderItems)
        {
            TempData["Error"] =
                "This product cannot be permanently deleted because it is associated with an existing order. Deactivate it instead.";

            return RedirectToAction(nameof(Manage));
        }

        var cartItems =
            await _context.CartItems
                .Where(ci =>
                    ci.ProductId == id)
                .ToListAsync();

        if (cartItems.Count > 0)
        {
            _context.CartItems.RemoveRange(
                cartItems);
        }

        var wishlists =
            await _context.Wishlists
                .Where(w =>
                    w.ProductId == id)
                .ToListAsync();

        if (wishlists.Count > 0)
        {
            _context.Wishlists.RemoveRange(
                wishlists);
        }

        await DeleteCloudinaryImage(
            product.MainImageUrl);

        foreach (var productImage
                 in product.ProductImages)
        {
            await DeleteCloudinaryImage(
                productImage.ImageUrl);
        }

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
            Path.GetExtension(
                file.FileName)
                .ToLowerInvariant();

        return allowedExtensions.Contains(
            extension);
    }

    // =========================================================
    // CLOUDINARY UPLOAD
    // =========================================================

    private async Task<string> UploadImage(
        IFormFile imageFile)
    {
        await using var stream =
            imageFile.OpenReadStream();

        var extension =
            Path.GetExtension(
                imageFile.FileName)
                .ToLowerInvariant();

        var publicId =
            $"criskidswear/products/{Guid.NewGuid():N}";

        var uploadParams =
            new ImageUploadParams
            {
                File =
                    new FileDescription(
                        imageFile.FileName,
                        stream),

                PublicId = publicId,

                Folder =
                    "criskidswear/products",

                Overwrite = false
            };

        var result =
            await _cloudinary.UploadAsync(
                uploadParams);

        if (result.Error != null)
        {
            throw new InvalidOperationException(
                $"Cloudinary upload failed: {result.Error.Message}");
        }

        if (string.IsNullOrWhiteSpace(
                result.SecureUrl?.ToString()))
        {
            throw new InvalidOperationException(
                "Cloudinary upload completed but no image URL was returned.");
        }

        return result.SecureUrl.ToString();
    }

    // =========================================================
    // CLOUDINARY DELETE
    // =========================================================

    private async Task DeleteCloudinaryImage(
        string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        if (!imageUrl.Contains(
                "res.cloudinary.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var publicId =
                ExtractCloudinaryPublicId(
                    imageUrl);

            if (string.IsNullOrWhiteSpace(
                    publicId))
            {
                return;
            }

            var deleteParams =
                new DeletionParams(
                    publicId)
                {
                    ResourceType =
                        ResourceType.Image
                };

            await _cloudinary.DestroyAsync(
                deleteParams);
        }
        catch
        {
            // Image deletion should not prevent
            // the database operation from completing.
        }
    }

    // =========================================================
    // EXTRACT CLOUDINARY PUBLIC ID
    // =========================================================

    private string? ExtractCloudinaryPublicId(
        string imageUrl)
    {
        try
        {
            var uri =
                new Uri(imageUrl);

            var path =
                uri.AbsolutePath;

            const string uploadMarker =
                "/image/upload/";

            var uploadIndex =
                path.IndexOf(
                    uploadMarker,
                    StringComparison.OrdinalIgnoreCase);

            if (uploadIndex < 0)
            {
                return null;
            }

            var publicPart =
                path.Substring(
                    uploadIndex +
                    uploadMarker.Length);

            var segments =
                publicPart.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                return null;
            }

            var startIndex = 0;

            // Remove Cloudinary version segment.
            if (segments[0].StartsWith(
                    "v",
                    StringComparison.OrdinalIgnoreCase) &&
                segments[0].Length > 1 &&
                long.TryParse(
                    segments[0].Substring(1),
                    out _))
            {
                startIndex = 1;
            }

            if (startIndex >= segments.Length)
            {
                return null;
            }

            var publicId =
                string.Join(
                    "/",
                    segments.Skip(startIndex));

            var extension =
                Path.GetExtension(publicId);

            if (!string.IsNullOrWhiteSpace(
                    extension))
            {
                publicId =
                    publicId.Substring(
                        0,
                        publicId.Length -
                        extension.Length);
            }

            return publicId;
        }
        catch
        {
            return null;
        }
    }
}