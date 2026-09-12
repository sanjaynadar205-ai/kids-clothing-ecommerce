using KidsWearStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidsWearStore.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private const int MaxProductManagers = 4;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var userRoles = new Dictionary<string, IList<string>>();
        foreach (var user in users)
            userRoles[user.Id] = await _userManager.GetRolesAsync(user);

        ViewBag.UserRoles = userRoles;
        ViewBag.ProductManagerCount = userRoles.Values.Count(r => r.Contains("ProductManager"));
        ViewBag.MaxProductManagers = MaxProductManagers;
        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignProductManager(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            TempData["Error"] = "An Admin cannot be changed to Product Manager.";
            return RedirectToAction(nameof(Index));
        }

        if (await _userManager.IsInRoleAsync(user, "ProductManager"))
        {
            TempData["Info"] = $"{user.Email} is already a Product Manager.";
            return RedirectToAction(nameof(Index));
        }

        var managerCount = await CountProductManagersAsync();
        if (managerCount >= MaxProductManagers)
        {
            TempData["Error"] = "The maximum of 4 Product Managers has already been reached.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _userManager.AddToRoleAsync(user, "ProductManager");
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"{user.Email} is now a Product Manager.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveProductManager(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var result = await _userManager.RemoveFromRoleAsync(user, "ProductManager");
        if (!result.Succeeded)
            TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
        else
            TempData["Success"] = $"{user.Email} is no longer a Product Manager.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<int> CountProductManagersAsync()
    {
        var users = await _userManager.GetUsersInRoleAsync("ProductManager");
        return users.Count;
    }
}
