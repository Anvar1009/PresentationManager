using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IProjectRepository"/> - deliberately has no business logic of
/// its own (validation, participant aggregation, cascading file cleanup on delete all stay in
/// PresentationManager.Application.Services.ProjectService, which PresentationManager.UI keeps running
/// unchanged, composing this controller's raw endpoints exactly the way it used to compose the local EF
/// repository).</summary>
[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;

    public ProjectsController(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProjectDto>>> GetAll([FromQuery] int? createdBy, CancellationToken ct)
    {
        var projects = createdBy is int userId
            ? await _projectRepository.GetByCreatorAsync(userId, ct)
            : await _projectRepository.GetAllAsync(ct);

        return Ok(projects.Select(ProjectDto.FromEntity).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProjectDto>> GetById(int id, CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdAsync(id, ct);
        return project is null ? NotFound() : Ok(ProjectDto.FromEntity(project));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Add(CreateProjectRequest request, CancellationToken ct)
    {
        var project = new Project
        {
            Name = request.Name,
            EventStartDate = request.EventStartDate,
            EventEndDate = request.EventEndDate,
            EventTime = request.EventTime,
            Location = request.Location,
            CreatedByUserId = request.CreatedByUserId
        };
        var created = await _projectRepository.AddAsync(project, ct);
        return Ok(ProjectDto.FromEntity(created));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _projectRepository.DeleteAsync(id, ct);
        return NoContent();
    }
}
