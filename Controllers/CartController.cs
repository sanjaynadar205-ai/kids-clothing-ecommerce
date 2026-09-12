using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

[Authorize]
public class CartController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public CartController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // ----------------------------------------------------
    // CART
    // ----------------------------------------------------

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var cart = await _context.Carts
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
            .FirstOrDefaultAsync(c => c.UserId == user.Id);

        if (cart == null)
        {
            return View(new List<CartItem>());
        }

        // Only display cart items whose products still exist.
        var validItems = cart.CartItems
            .Where(ci => ci.Product != null)
            .ToList();

        return View(validItems);
    }


    // ----------------------------------------------------
    // ADD TO CART
    // ----------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(
        int productId,
        int quantity = 1)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        if (quantity < 1)
        {
            quantity = 1;
        }

        var product = await _context.Products
            .FirstOrDefaultAsync(
                p => p.Id == productId &&
                     p.IsActive);

        if (product == null)
        {
            TempData["CartError"] =
                "Product not found.";

            return RedirectToAction(
                "Index",
                "Products");
        }

        if (product.StockQuantity <= 0)
        {
            TempData["CartError"] =
                "This product is currently out of stock.";

            return RedirectToAction(
                "Details",
                "Products",
                new { id = productId });
        }

        // Find the user's cart.
        var cart = await _context.Carts
            .Include(c => c.CartItems)
            .FirstOrDefaultAsync(
                c => c.UserId == user.Id);

        // Create cart if it does not exist.
        if (cart == null)
        {
            cart = new Cart
            {
                UserId = user.Id
            };

            _context.Carts.Add(cart);

            await _context.SaveChangesAsync();
        }

        // Check if product is already in cart.
        var existingItem = cart.CartItems
            .FirstOrDefault(
                ci => ci.ProductId == productId);

        if (existingItem != null)
        {
            var newQuantity =
                existingItem.Quantity + quantity;

            if (newQuantity > product.StockQuantity)
            {
                newQuantity =
                    product.StockQuantity;
            }

            existingItem.Quantity =
                newQuantity;
        }
        else
        {
            var cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductId = product.Id,
                Quantity = Math.Min(
                    quantity,
                    product.StockQuantity)
            };

            cart.CartItems.Add(cartItem);
        }

        await _context.SaveChangesAsync();

        TempData["CartSuccess"] =
            $"{product.Name} added to your cart.";

        return RedirectToAction(nameof(Index));
    }


    // ----------------------------------------------------
    // UPDATE QUANTITY
    // ----------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(
        int cartItemId,
        int quantity)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var cartItem = await _context.CartItems
            .Include(ci => ci.Cart)
            .Include(ci => ci.Product)
            .FirstOrDefaultAsync(
                ci => ci.Id == cartItemId);

        if (cartItem == null)
        {
            return NotFound();
        }

        // Explicitly verify that the cart belongs to
        // the currently authenticated user.
        if (cartItem.Cart == null ||
            cartItem.Cart.UserId != user.Id)
        {
            return NotFound();
        }

        if (cartItem.Product == null)
        {
            return NotFound();
        }

        if (quantity <= 0)
        {
            _context.CartItems.Remove(cartItem);
        }
        else
        {
            if (quantity >
                cartItem.Product.StockQuantity)
            {
                quantity =
                    cartItem.Product.StockQuantity;
            }

            cartItem.Quantity =
                quantity;
        }

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }


    // ----------------------------------------------------
    // REMOVE FROM CART
    // ----------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        int cartItemId)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var cartItem = await _context.CartItems
            .Include(ci => ci.Cart)
            .FirstOrDefaultAsync(
                ci => ci.Id == cartItemId);

        if (cartItem == null)
        {
            return NotFound();
        }

        // Explicitly verify ownership before removing.
        if (cartItem.Cart == null ||
            cartItem.Cart.UserId != user.Id)
        {
            return NotFound();
        }

        _context.CartItems.Remove(cartItem);

        await _context.SaveChangesAsync();

        TempData["CartSuccess"] =
            "Product removed from your cart.";

        return RedirectToAction(nameof(Index));
    }


    // ----------------------------------------------------
    // CLEAR CART
    // ----------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var cart = await _context.Carts
            .Include(c => c.CartItems)
            .FirstOrDefaultAsync(
                c => c.UserId == user.Id);

        if (cart != null &&
            cart.CartItems.Any())
        {
            _context.CartItems.RemoveRange(
                cart.CartItems);

            await _context.SaveChangesAsync();
        }

        TempData["CartSuccess"] =
            "Your cart has been cleared.";

        return RedirectToAction(nameof(Index));
    }
}