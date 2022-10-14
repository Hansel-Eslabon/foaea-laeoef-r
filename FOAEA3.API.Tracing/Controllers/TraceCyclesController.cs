﻿using FOAEA3.Business.Areas.Application;
using FOAEA3.Model;
using FOAEA3.Model.Constants;
using FOAEA3.Model.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FOAEA3.API.Tracing.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TraceCyclesController : ControllerBase
{
    private readonly CustomConfig config;

    public TraceCyclesController(IOptions<CustomConfig> config)
    {
        this.config = config.Value;
    }

    [HttpGet("Version")]
    public ActionResult<string> GetVersion() => Ok("TraceCycles API Version 1.0");

    [HttpGet("DB")]
    [Authorize(Roles = Roles.Admin)]
    public ActionResult<string> GetDatabase([FromServices] IRepositories repositories) => Ok(repositories.MainDB.ConnectionString);

    [HttpGet("")]
    public async Task<ActionResult<List<TraceCycleQuantityData>>> GetTraceCycleQuantityData(
                                                            [FromQuery] string enforcementServiceCode,
                                                            [FromQuery] string fileCycle,
                                                            [FromServices] IRepositories repositories)
    {
        var manager = new TracingManager(repositories, config);

        var data = await manager.GetTraceCycleQuantityDataAsync(enforcementServiceCode, fileCycle);

        return Ok(data);

    }
}
