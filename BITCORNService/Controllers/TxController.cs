﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BITCORNService.Models;
using BITCORNService.Utils.LockUser;
using BITCORNService.Utils.Models;
using BITCORNService.Utils.Tx;
using Microsoft.AspNetCore.Mvc;

namespace BITCORNService.Controllers
{
    [ServiceFilter(typeof(LockUserAttribute))]
    [Route("api/[controller]")]
    [ApiController]
    public class TxController : ControllerBase
    {
        private readonly BitcornContext _dbContext;

        public TxController(BitcornContext dbContext)
        {
            _dbContext = dbContext;
        }
        [HttpPost("rain")]
        public async Task Rain([FromBody] RainBody rainBody)
        {
            try
            {
                var txUsers = rainBody.TxUsers;
                if (txUsers == null || !txUsers.Any()) throw new ArgumentNullException();
                //array of {amount, id}
                await TxUtils.ExecuteRainTxs(txUsers, _dbContext);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }


            //recipient response TODO
        }

        [HttpPost("payout")]
        public async Task Payout([FromBody] IEnumerable<TxUser> txUsers)
        {
            if (txUsers == null) throw new ArgumentNullException();
            //array of {amount, id}
            await TxUtils.ExecuteRainTxs(txUsers, _dbContext);
            //senderresponses TODO
        }

        [HttpPost("tipcorn")]
        public async Task Tipcorn([FromBody] TxUser txUser)
        {
            if (txUser == null) throw new ArgumentNullException();
            if (txUser.Id == null) throw new ArgumentNullException();
            if (txUser.Amount == 0) throw new ArgumentNullException();

            //sender twitchid, receiver twitchid, amount
            await TxUtils.ExecuteTipTx(txUser, _dbContext);

            //senderresponse TODO
        }

        [HttpPost("withdraw")]
        public async Task Withdraw([FromBody] WithdrawUser withdrawUser)
        {
            //sender twitchid, cornaddy, amount
            await TxUtils.ExecuteDebitTx(withdrawUser, _dbContext);

            //call to wallet to properly withdraw TODO

            //tx id TODO
        }



    }
}
