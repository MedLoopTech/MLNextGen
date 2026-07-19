using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers.Pos;

[ApiController]
[Route("api/pos/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomersController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<List<Customer>>> GetAll([FromQuery] string? search)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var query = _db.Customers.AsNoTracking().Where(c => c.PharmacyId == user.PharmacyId && c.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                c.Name.Contains(search) ||
                (c.PhoneNumber != null && c.PhoneNumber.Contains(search)));
        }

        return Ok(await query.OrderBy(c => c.Name).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetById(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _db.Customers.FindAsync(id);

        if (customer is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != customer.PharmacyId)
        {
            return Forbid();
        }

        return Ok(customer);
    }

    public record CreateCustomerRequest(string Name, string? PhoneNumber, string? Email);

    [HttpPost]
    public async Task<ActionResult<Customer>> Create([FromBody] CreateCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var customer = new Customer
        {
            PharmacyId = user.PharmacyId,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            Email = request.Email
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _db.Customers.FindAsync(id);

        if (customer is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != customer.PharmacyId)
        {
            return Forbid();
        }

        customer.IsActive = false;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
