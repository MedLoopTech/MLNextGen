using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationsController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<Notification>>> GetMine([FromQuery] bool unreadOnly = false)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var query = _db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == user.Id);
        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var notifications = await query.OrderByDescending(n => n.CreatedAt).ToListAsync();
        return Ok(notifications);
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var notification = await _db.Notifications.FindAsync(id);

        if (notification is null)
        {
            return NotFound();
        }

        if (notification.RecipientUserId != user?.Id)
        {
            return Forbid();
        }

        notification.IsRead = true;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var unread = await _db.Notifications
            .Where(n => n.RecipientUserId == user.Id && !n.IsRead)
            .ToListAsync();

        foreach (var notification in unread)
        {
            notification.IsRead = true;
        }

        await _db.SaveChangesAsync();

        return NoContent();
    }
}
