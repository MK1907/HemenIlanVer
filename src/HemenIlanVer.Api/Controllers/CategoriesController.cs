using HemenIlanVer.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HemenIlanVer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public sealed class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categories;

    public CategoriesController(ICategoryService categories) => _categories = categories;

    [HttpGet]
    public async Task<IActionResult> GetTree(CancellationToken ct)
    {
        var tree = await _categories.GetTreeAsync(ct);
        return Ok(tree);
    }

    [HttpGet("{id:guid}/attributes")]
    public async Task<IActionResult> GetAttributes(Guid id, CancellationToken ct)
    {
        var attrs = await _categories.GetAttributesAsync(id, ct);
        return Ok(attrs);
    }
}
