using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db)
    {
        _db = db;
    }

    // Reads are open — categories are just marketplace taxonomy, not
    // tenant-scoped data, so unlike Branches/Warehouses there's nothing
    // here that needs an [Authorize] gate for GET.
    [HttpGet]
    public async Task<ActionResult<List<Category>>> GetAll()
    {
        return Ok(await _db.Categories.AsNoTracking().OrderBy(c => c.Title).ToListAsync());
    }

    public record CreateCategoryRequest(string Title, string? ImageUrl, string? Color);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<Category>> Create([FromBody] CreateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Title is required.");
        }

        var category = new Category
        {
            Title = request.Title,
            ImageUrl = request.ImageUrl,
            Color = request.Color
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), category);
    }
}
