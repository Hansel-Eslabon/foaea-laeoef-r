﻿using FOAEA3.Business.Areas.Financials;
using FOAEA3.Model;
using FOAEA3.Model.Constants;
using FOAEA3.Model.Interfaces.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FOAEA3.API.Interception.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class DivertFundsController : ControllerBase
    {
        [HttpGet("")]
        [Authorize(Policy = Policies.ApplicationReadAccess)]
        public async Task<ActionResult<List<DivertFundData>>> GetDivertFunds([FromQuery] string enfSrv,
                                                                             [FromQuery] string batchId,
                                                                             [FromServices] IRepositories db,
                                                                             [FromServices] IRepositories_Finance dbFinance)
        {
            var manager = new FinancialManager(db, dbFinance);
            return Ok(await manager.GetDivertFundsData(enfSrv, batchId));
        }
    }
}