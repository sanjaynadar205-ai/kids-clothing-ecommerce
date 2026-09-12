using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KidsWearStore.Data;
using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

[Authorize]
public class CheckoutController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CheckoutController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _context = context;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    // ====================================================
    // CHECKOUT PAGE
    // ====================================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var cart = await GetUserCart(user.Id);

        if (cart == null || !cart.CartItems.Any())
        {
            TempData["CartError"] = "Your cart is empty.";

            return RedirectToAction(
                "Index",
                "Cart");
        }

        var validItems = cart.CartItems
            .Where(ci =>
                ci.Product != null &&
                ci.Product.IsActive)
            .ToList();

        if (!validItems.Any())
        {
            TempData["CartError"] =
                "Your cart does not contain any available products.";

            return RedirectToAction(
                "Index",
                "Cart");
        }

        foreach (var item in validItems)
        {
            if (item.Product!.StockQuantity < item.Quantity)
            {
                TempData["CartError"] =
                    $"Not enough stock available for {item.Product.Name}.";

                return RedirectToAction(
                    "Index",
                    "Cart");
            }
        }

        SetCheckoutViewData(
            user,
            validItems);

        return View();
    }

    // ====================================================
    // CREATE ORDER
    // ====================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(
        string customerName,
        string customerEmail,
        string customerPhone,
        string shippingAddress,
        string city,
        string state,
        string postalCode,
        string paymentMethod)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Unauthorized();
        }

        // -----------------------------------------------
        // VALIDATION
        // -----------------------------------------------

        if (string.IsNullOrWhiteSpace(customerName))
            ModelState.AddModelError(
                "customerName",
                "Please enter your name.");

        if (string.IsNullOrWhiteSpace(customerEmail))
            ModelState.AddModelError(
                "customerEmail",
                "Please enter your email.");

        if (string.IsNullOrWhiteSpace(customerPhone))
            ModelState.AddModelError(
                "customerPhone",
                "Please enter your phone number.");

        if (string.IsNullOrWhiteSpace(shippingAddress))
            ModelState.AddModelError(
                "shippingAddress",
                "Please enter your shipping address.");

        if (string.IsNullOrWhiteSpace(city))
            ModelState.AddModelError(
                "city",
                "Please enter your city.");

        if (string.IsNullOrWhiteSpace(state))
            ModelState.AddModelError(
                "state",
                "Please enter your state.");

        if (string.IsNullOrWhiteSpace(postalCode))
            ModelState.AddModelError(
                "postalCode",
                "Please enter your postal code.");

        if (paymentMethod != "Cash on Delivery" &&
            paymentMethod != "Prepaid")
        {
            ModelState.AddModelError(
                "paymentMethod",
                "Invalid payment method.");
        }

        // -----------------------------------------------
        // LOAD CART
        // -----------------------------------------------

        var cart = await GetUserCart(user.Id);

        if (cart == null || !cart.CartItems.Any())
        {
            return Json(new
            {
                success = false,
                message = "Your cart is empty."
            });
        }

        var cartItems = cart.CartItems
            .Where(ci =>
                ci.Product != null &&
                ci.Product.IsActive)
            .ToList();

        if (!cartItems.Any())
        {
            return Json(new
            {
                success = false,
                message = "Your cart contains no available products."
            });
        }

        // -----------------------------------------------
        // STOCK CHECK
        // -----------------------------------------------

        foreach (var item in cartItems)
        {
            if (item.Product!.StockQuantity < item.Quantity)
            {
                return Json(new
                {
                    success = false,
                    message =
                        $"Not enough stock for {item.Product.Name}. " +
                        $"Only {item.Product.StockQuantity} item(s) available."
                });
            }
        }

        // -----------------------------------------------
        // VALIDATION FAILURE
        // -----------------------------------------------

        if (!ModelState.IsValid)
        {
            return Json(new
            {
                success = false,
                message = "Please check your checkout information."
            });
        }

        // -----------------------------------------------
        // CALCULATE TOTAL
        // -----------------------------------------------

        decimal subtotal = 0;

        foreach (var item in cartItems)
        {
            subtotal +=
                GetProductPrice(item.Product!) *
                item.Quantity;
        }

        decimal discount = 0;

        decimal shipping =
            subtotal >= 999
                ? 0
                : 49;

        decimal total =
            subtotal -
            discount +
            shipping;

        // -----------------------------------------------
        // GENERATE ORDER NUMBER
        // -----------------------------------------------

        string orderNumber;

        do
        {
            orderNumber =
                $"KWS-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
        }
        while (await _context.Orders.AnyAsync(
            o => o.OrderNumber == orderNumber));

        // -----------------------------------------------
        // CREATE LOCAL ORDER
        // -----------------------------------------------

        var order = new Order
        {
            OrderNumber = orderNumber,

            UserId = user.Id,

            CustomerName =
                customerName.Trim(),

            CustomerEmail =
                customerEmail.Trim(),

            CustomerPhone =
                customerPhone.Trim(),

            ShippingAddress =
                shippingAddress.Trim(),

            City =
                city.Trim(),

            State =
                state.Trim(),

            PostalCode =
                postalCode.Trim(),

            Subtotal = subtotal,

            Discount = discount,

            TotalAmount = total,

            PaymentMethod =
                paymentMethod,

            PaymentStatus =
                paymentMethod == "Cash on Delivery"
                    ? "Pending"
                    : "Created",

            Status =
                OrderStatus.Pending,

            CreatedAt =
                DateTime.UtcNow
        };

        _context.Orders.Add(order);

        await _context.SaveChangesAsync();

        // -----------------------------------------------
        // COD
        // -----------------------------------------------

        if (paymentMethod == "Cash on Delivery")
        {
            await CompleteCashOnDeliveryOrder(
                order,
                cartItems);

            await UpdateUserAddress(
                user,
                customerPhone,
                shippingAddress,
                city,
                state,
                postalCode);

            return Json(new
            {
                success = true,
                redirectUrl =
                    Url.Action(
                        nameof(Confirmation),
                        new
                        {
                            orderNumber =
                                order.OrderNumber
                        })
            });
        }

        // -----------------------------------------------
        // PREPAID
        //
        // IMPORTANT:
        // Create the OrderItem snapshot NOW.
        // We do not reduce stock yet.
        // Stock is reduced only after Razorpay payment
        // has been successfully captured.
        // -----------------------------------------------

        try
        {
            await CreatePrepaidOrderItems(
                order,
                cartItems);

            var razorpayOrderId =
                await CreateRazorpayOrder(
                    order.OrderNumber,
                    total);

            order.RazorpayOrderId =
                razorpayOrderId;

            order.PaymentStatus =
                "Created";

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                prepaid = true,

                keyId =
                    _configuration[
                        "Razorpay:KeyId"],

                razorpayOrderId =
                    razorpayOrderId,

                amount =
                    Convert.ToInt64(
                        total * 100),

                currency = "INR",

                orderNumber =
                    order.OrderNumber,

                customerName =
                    order.CustomerName,

                customerEmail =
                    order.CustomerEmail,

                customerPhone =
                    order.CustomerPhone
            });
        }
        catch
        {
            order.PaymentStatus =
                "Failed";

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = false,
                message =
                    "Unable to start online payment. Please try again."
            });
        }
    }

    // ====================================================
    // VERIFY RAZORPAY PAYMENT
    // ====================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyPayment(
        string orderNumber,
        string razorpayOrderId,
        string razorpayPaymentId,
        string razorpaySignature)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Unauthorized();
        }

        var order = await _context.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(
                o =>
                    o.OrderNumber == orderNumber &&
                    o.UserId == user.Id);

        if (order == null)
        {
            return Json(new
            {
                success = false,
                message = "Order not found."
            });
        }

        // -----------------------------------------------
        // ALREADY COMPLETED PAYMENT
        // -----------------------------------------------

        if (order.PaymentStatus == "Paid" &&
            order.OrderItems.Any())
        {
            return Json(new
            {
                success = true,

                redirectUrl =
                    Url.Action(
                        nameof(Confirmation),
                        new
                        {
                            orderNumber =
                                order.OrderNumber
                        })
            });
        }

        // -----------------------------------------------
        // VALIDATE RAZORPAY ORDER
        // -----------------------------------------------

        if (string.IsNullOrWhiteSpace(order.RazorpayOrderId) ||
            order.RazorpayOrderId != razorpayOrderId)
        {
            return Json(new
            {
                success = false,
                message = "Invalid Razorpay order."
            });
        }

        if (string.IsNullOrWhiteSpace(razorpayPaymentId) ||
            string.IsNullOrWhiteSpace(razorpaySignature))
        {
            return Json(new
            {
                success = false,
                message = "Invalid payment information."
            });
        }

        // -----------------------------------------------
        // VERIFY SIGNATURE
        // -----------------------------------------------

        var signatureValid =
            VerifyRazorpaySignature(
                order.RazorpayOrderId,
                razorpayPaymentId,
                razorpaySignature);

        if (!signatureValid)
        {
            order.PaymentStatus =
                "Failed";

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = false,
                message =
                    "Payment verification failed."
            });
        }

        // -----------------------------------------------
        // VERIFY PAYMENT STATUS WITH RAZORPAY
        // -----------------------------------------------

        var paymentStatus =
            await GetRazorpayPaymentStatus(
                razorpayPaymentId);

        if (paymentStatus != "captured")
        {
            order.PaymentStatus =
                paymentStatus ?? "Unknown";

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = false,
                message =
                    "Payment has not been captured yet."
            });
        }

        // -----------------------------------------------
        // ENSURE ORDER ITEMS EXIST
        // -----------------------------------------------

        if (!order.OrderItems.Any())
        {
            order.PaymentStatus =
                "Paid - Missing Items";

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = false,
                message =
                    "Payment succeeded, but the order items could not be found. " +
                    "Please contact the store before placing another order."
            });
        }

        // -----------------------------------------------
        // LOAD PRODUCTS FOR ORDER ITEMS
        // -----------------------------------------------

        var productIds =
            order.OrderItems
                .Select(oi => oi.ProductId)
                .Distinct()
                .ToList();

        var products =
            await _context.Products
                .Where(p =>
                    productIds.Contains(p.Id))
                .ToDictionaryAsync(
                    p => p.Id);

        // -----------------------------------------------
        // CHECK STOCK AGAIN
        // -----------------------------------------------

        foreach (var orderItem in order.OrderItems)
        {
            if (!products.TryGetValue(
                    orderItem.ProductId,
                    out var product))
            {
                order.PaymentStatus =
                    "Paid - Product Issue";

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = false,
                    message =
                        "Payment succeeded, but one of the products " +
                        "could not be found. Please contact the store."
                });
            }

            if (!product.IsActive)
            {
                order.PaymentStatus =
                    "Paid - Product Unavailable";

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = false,
                    message =
                        $"Payment succeeded, but {product.Name} " +
                        "is no longer available. Please contact the store."
                });
            }

            if (product.StockQuantity < orderItem.Quantity)
            {
                order.PaymentStatus =
                    "Paid - Stock Issue";

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = false,
                    message =
                        $"Payment succeeded, but {product.Name} " +
                        "is no longer available in the required quantity. " +
                        "Please contact the store."
                });
            }
        }

        // -----------------------------------------------
        // SAVE PAYMENT INFORMATION
        // -----------------------------------------------

        order.RazorpayPaymentId =
            razorpayPaymentId;

        order.RazorpaySignature =
            razorpaySignature;

        order.PaymentStatus =
            "Paid";

        order.PaymentMethod =
            "Prepaid";

        // -----------------------------------------------
        // COMPLETE PREPAID ORDER
        //
        // Uses the OrderItems saved when the order was
        // originally created. It does NOT depend on the
        // customer's current cart.
        // -----------------------------------------------

        await CompletePrepaidOrder(
            order,
            products);

        await UpdateUserAddress(
            user,
            order.CustomerPhone,
            order.ShippingAddress,
            order.City,
            order.State,
            order.PostalCode);

        return Json(new
        {
            success = true,

            redirectUrl =
                Url.Action(
                    nameof(Confirmation),
                    new
                    {
                        orderNumber =
                            order.OrderNumber
                    })
        });
    }

    // ====================================================
    // CONFIRMATION
    // ====================================================

    [HttpGet]
    public async Task<IActionResult> Confirmation(
        string? orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return RedirectToAction(
                "Index",
                "Home");
        }

        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge();
        }

        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(
                o =>
                    o.OrderNumber == orderNumber &&
                    o.UserId == user.Id);

        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    // ====================================================
    // CREATE PREPAID ORDER ITEMS
    //
    // Creates a snapshot of the cart.
    // Stock is NOT reduced here.
    // ====================================================

    private async Task CreatePrepaidOrderItems(
        Order order,
        List<CartItem> cartItems)
    {
        foreach (var cartItem in cartItems)
        {
            var product =
                cartItem.Product!;

            var unitPrice =
                GetProductPrice(product);

            var orderItem =
                new OrderItem
                {
                    OrderId =
                        order.Id,

                    ProductId =
                        product.Id,

                    ProductName =
                        product.Name,

                    UnitPrice =
                        unitPrice,

                    Quantity =
                        cartItem.Quantity,

                    TotalPrice =
                        unitPrice *
                        cartItem.Quantity
                };

            _context.OrderItems.Add(
                orderItem);
        }

        await _context.SaveChangesAsync();
    }

    // ====================================================
    // COMPLETE COD ORDER
    // ====================================================

    private async Task CompleteCashOnDeliveryOrder(
        Order order,
        List<CartItem> cartItems)
    {
        // Prevent duplicate completion.
        if (order.OrderItems.Any())
        {
            return;
        }

        foreach (var cartItem in cartItems)
        {
            var product =
                cartItem.Product!;

            var unitPrice =
                GetProductPrice(product);

            var orderItem =
                new OrderItem
                {
                    OrderId =
                        order.Id,

                    ProductId =
                        product.Id,

                    ProductName =
                        product.Name,

                    UnitPrice =
                        unitPrice,

                    Quantity =
                        cartItem.Quantity,

                    TotalPrice =
                        unitPrice *
                        cartItem.Quantity
                };

            _context.OrderItems.Add(
                orderItem);

            product.StockQuantity -=
                cartItem.Quantity;

            product.UpdatedAt =
                DateTime.UtcNow;
        }

        _context.CartItems.RemoveRange(
            cartItems);

        order.Status =
            OrderStatus.Pending;

        order.UpdatedAt =
            DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    // ====================================================
    // COMPLETE PREPAID ORDER
    // ====================================================

    private async Task CompletePrepaidOrder(
        Order order,
        Dictionary<int, Product> products)
    {
        // If already completed, do nothing.
        if (order.Status == OrderStatus.Pending &&
            order.PaymentStatus == "Paid" &&
            order.UpdatedAt.HasValue)
        {
            // We still continue because UpdatedAt may have
            // been set earlier during payment processing.
        }

        foreach (var orderItem in order.OrderItems)
        {
            if (!products.TryGetValue(
                    orderItem.ProductId,
                    out var product))
            {
                throw new InvalidOperationException(
                    $"Product {orderItem.ProductId} was not found.");
            }

            product.StockQuantity -=
                orderItem.Quantity;

            product.UpdatedAt =
                DateTime.UtcNow;
        }

        // -----------------------------------------------
        // REMOVE THE PURCHASED ITEMS FROM THE CART
        // -----------------------------------------------

        var cart = await _context.Carts
            .Include(c => c.CartItems)
            .FirstOrDefaultAsync(
                c => c.UserId == order.UserId);

        if (cart != null)
        {
            var purchasedProductIds =
                order.OrderItems
                    .Select(oi => oi.ProductId)
                    .ToHashSet();

            var cartItemsToRemove =
                cart.CartItems
                    .Where(ci =>
                        purchasedProductIds.Contains(
                            ci.ProductId))
                    .ToList();

            if (cartItemsToRemove.Any())
            {
                _context.CartItems.RemoveRange(
                    cartItemsToRemove);
            }
        }

        order.Status =
            OrderStatus.Pending;

        order.UpdatedAt =
            DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    // ====================================================
    // RAZORPAY ORDER CREATION
    // ====================================================

    private async Task<string> CreateRazorpayOrder(
        string receipt,
        decimal amount)
    {
        var keyId =
            _configuration["Razorpay:KeyId"];

        var keySecret =
            _configuration["Razorpay:KeySecret"];

        if (string.IsNullOrWhiteSpace(keyId) ||
            string.IsNullOrWhiteSpace(keySecret))
        {
            throw new InvalidOperationException(
                "Razorpay API credentials are not configured.");
        }

        var client =
            _httpClientFactory.CreateClient();

        var auth =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $"{keyId}:{keySecret}"));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                auth);

        var requestBody = new
        {
            amount =
                Convert.ToInt64(amount * 100),

            currency = "INR",

            receipt = receipt,

            notes = new
            {
                store = "CrisKidsWear"
            }
        };

        var json =
            JsonSerializer.Serialize(requestBody);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

        var response =
            await client.PostAsync(
                "https://api.razorpay.com/v1/orders",
                content);

        var responseBody =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Razorpay order creation failed: {responseBody}");
        }

        using var document =
            JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty(
                "id",
                out var idElement))
        {
            throw new InvalidOperationException(
                "Razorpay did not return an order ID.");
        }

        return idElement.GetString()
               ?? throw new InvalidOperationException(
                   "Invalid Razorpay order ID.");
    }

    // ====================================================
    // GET PAYMENT STATUS
    // ====================================================

    private async Task<string?> GetRazorpayPaymentStatus(
        string paymentId)
    {
        var keyId =
            _configuration["Razorpay:KeyId"];

        var keySecret =
            _configuration["Razorpay:KeySecret"];

        if (string.IsNullOrWhiteSpace(keyId) ||
            string.IsNullOrWhiteSpace(keySecret))
        {
            return null;
        }

        var client =
            _httpClientFactory.CreateClient();

        var auth =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $"{keyId}:{keySecret}"));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                auth);

        var response =
            await client.GetAsync(
                $"https://api.razorpay.com/v1/payments/{paymentId}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body =
            await response.Content.ReadAsStringAsync();

        using var document =
            JsonDocument.Parse(body);

        if (document.RootElement.TryGetProperty(
                "status",
                out var statusElement))
        {
            return statusElement.GetString();
        }

        return null;
    }

    // ====================================================
    // SIGNATURE VERIFICATION
    // ====================================================

    private bool VerifyRazorpaySignature(
        string razorpayOrderId,
        string razorpayPaymentId,
        string razorpaySignature)
    {
        var secret =
            _configuration["Razorpay:KeySecret"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var payload =
            $"{razorpayOrderId}|{razorpayPaymentId}";

        using var hmac =
            new HMACSHA256(
                Encoding.UTF8.GetBytes(secret));

        var hash =
            hmac.ComputeHash(
                Encoding.UTF8.GetBytes(payload));

        var generatedSignature =
            Convert.ToHexString(hash)
                .ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(
                generatedSignature),
            Encoding.UTF8.GetBytes(
                razorpaySignature));
    }

    // ====================================================
    // USER CART
    // ====================================================

    private async Task<Cart?> GetUserCart(
        string userId)
    {
        return await _context.Carts
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
            .FirstOrDefaultAsync(
                c => c.UserId == userId);
    }

    // ====================================================
    // PRICE
    // ====================================================

    private decimal GetProductPrice(
        Product product)
    {
        if (product.DiscountPrice.HasValue &&
            product.DiscountPrice.Value > 0 &&
            product.DiscountPrice.Value < product.Price)
        {
            return product.DiscountPrice.Value;
        }

        if (product.DiscountPercentage > 0)
        {
            var discount =
                product.Price *
                product.DiscountPercentage /
                100;

            return product.Price -
                   discount;
        }

        return product.Price;
    }

    // ====================================================
    // CHECKOUT VIEW DATA
    // ====================================================

    private void SetCheckoutViewData(
        ApplicationUser user,
        List<CartItem> items)
    {
        decimal subtotal = 0;

        foreach (var item in items)
        {
            subtotal +=
                GetProductPrice(
                    item.Product!) *
                item.Quantity;
        }

        decimal shipping =
            subtotal >= 999
                ? 0
                : 49;

        ViewBag.Subtotal =
            subtotal;

        ViewBag.Shipping =
            shipping;

        ViewBag.Total =
            subtotal + shipping;

        ViewBag.CartItems =
            items;

        ViewBag.CustomerName =
            $"{user.FirstName} {user.LastName}".Trim();

        ViewBag.CustomerEmail =
            user.Email ?? string.Empty;

        ViewBag.CustomerPhone =
            user.PhoneNumber ?? string.Empty;

        ViewBag.ShippingAddress =
            user.Address ?? string.Empty;

        ViewBag.City =
            user.City ?? string.Empty;

        ViewBag.State =
            user.State ?? string.Empty;

        ViewBag.PostalCode =
            user.PostalCode ?? string.Empty;
    }

    // ====================================================
    // SAVE CUSTOMER ADDRESS
    // ====================================================

    private async Task UpdateUserAddress(
        ApplicationUser user,
        string phone,
        string address,
        string city,
        string state,
        string postalCode)
    {
        user.PhoneNumber =
            phone.Trim();

        user.Address =
            address.Trim();

        user.City =
            city.Trim();

        user.State =
            state.Trim();

        user.PostalCode =
            postalCode.Trim();

        await _userManager.UpdateAsync(user);
    }
}