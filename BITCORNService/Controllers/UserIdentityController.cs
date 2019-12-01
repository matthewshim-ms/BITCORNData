﻿using System.Threading.Tasks;
using BITCORNService.Models;
using BITCORNService.Utils;
using Microsoft.AspNetCore.Mvc;

namespace BITCORNService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserIdentityController : ControllerBase
    {
        private readonly BitcornContext _dbContext;

        public UserIdentityController(BitcornContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpPost("{id}")]
        public async Task<UserIdentity> Auth0([FromRoute] string id)
        {
            var platformId =  BitcornUtils.GetPlatformId(id);
            return await BitcornUtils.GetUserIdentityForPlatform(platformId, _dbContext);
        }
    }
}