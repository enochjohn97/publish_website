using Folio.Web.Data;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Web.Controllers.Api;

[Route("api/images")]
public sealed class ImagesController : ControllerBase
{
    private readonly Repository _repository;

    public ImagesController(Repository repository)
    {
        _repository = repository;
    }

    [HttpGet("{id}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(string id)
    {
        if (!Guid.TryParse(id, out _)) return NotFound();
        var upload = await _repository.GetUploadAsync(id);
        if (upload is null) return NotFound();
        return File(upload.Value.Data, upload.Value.Mime);
    }
}
