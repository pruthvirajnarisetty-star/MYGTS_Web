using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using MygtsMvc.Models;

namespace MygtsMvc.Controllers;

[Authorize]
[Route("[controller]")]
[IgnoreAntiforgeryToken]
public sealed class DropdownOptionsController : Controller
{
    private readonly ApplicationDbContext _db;

    public DropdownOptionsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("GetAll")]
    public async Task<IActionResult> GetAll()
    {
        var options = await _db.CustomDropdownOptions
            .AsNoTracking()
            .OrderBy(o => o.Value)
            .ToListAsync();

        var grouped = options
            .GroupBy(o => o.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new { id = x.Id, value = x.Value }).ToList(),
                StringComparer.OrdinalIgnoreCase
            );

        return Json(new { success = true, branch = _db.CurrentBranch, options = grouped });
    }

    [HttpGet("GetByCategory")]
    public async Task<IActionResult> GetByCategory([FromQuery] string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Json(new { success = false, message = "Category is required." });
        }

        var items = await _db.CustomDropdownOptions
            .AsNoTracking()
            .Where(o => o.Category.ToLower() == category.Trim().ToLower())
            .OrderBy(o => o.Value)
            .Select(o => new { id = o.Id, value = o.Value })
            .ToListAsync();

        return Json(new { success = true, branch = _db.CurrentBranch, items });
    }

    public sealed class AddOptionModel
    {
        public string Category { get; set; } = "";
        public string Value { get; set; } = "";
    }

    [HttpPost("Add")]
    public async Task<IActionResult> Add([FromBody] AddOptionModel? body, [FromForm] string? category, [FromForm] string? value)
    {
        var cat = (!string.IsNullOrWhiteSpace(body?.Category) ? body.Category : category ?? "").Trim();
        var val = (!string.IsNullOrWhiteSpace(body?.Value) ? body.Value : value ?? "").Trim();

        if (string.IsNullOrWhiteSpace(cat))
        {
            return BadRequest(new { success = false, message = "Category is required." });
        }

        if (string.IsNullOrWhiteSpace(val))
        {
            return BadRequest(new { success = false, message = "Option value cannot be empty." });
        }

        if (val.Length > 100)
        {
            return BadRequest(new { success = false, message = "Option value cannot exceed 100 characters." });
        }

        // Check if duplicate in current branch
        var exists = await _db.CustomDropdownOptions
            .AnyAsync(o => o.Category.ToLower() == cat.ToLower() && o.Value.ToLower() == val.ToLower());

        if (exists)
        {
            return Conflict(new { success = false, message = $"The option '{val}' already exists for {cat}." });
        }

        var newOption = new CustomDropdownOption
        {
            Category = cat,
            Value = val,
            Branch = _db.CurrentBranch,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        _db.CustomDropdownOptions.Add(newOption);
        await _db.SaveChangesAsync();

        return Json(new { success = true, id = newOption.Id, category = newOption.Category, value = newOption.Value });
    }

    public sealed class DeleteOptionModel
    {
        public string Id { get; set; } = "";
    }

    [HttpPost("Delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteOptionModel? body, [FromForm] string? id)
    {
        var targetId = (!string.IsNullOrWhiteSpace(body?.Id) ? body.Id : id ?? "").Trim();
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return BadRequest(new { success = false, message = "Option ID is required." });
        }

        var item = await _db.CustomDropdownOptions.FirstOrDefaultAsync(o => o.Id == targetId);
        if (item is null)
        {
            return NotFound(new { success = false, message = "Option not found." });
        }

        _db.CustomDropdownOptions.Remove(item);
        await _db.SaveChangesAsync();

        return Json(new { success = true, message = "Option deleted successfully." });
    }
}
