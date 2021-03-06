﻿using System;
using System.Linq;
using System.Threading.Tasks;
using BITCORNService.Models;
using BITCORNService.Utils;
using BITCORNService.Utils.DbActions;
using BITCORNService.Utils.Models;
using BITCORNService.Utils.Tx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BITCORNService.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RegisterController : ControllerBase
    {
        private readonly BitcornContext _dbContext;
        private readonly IConfiguration _configuration;
        public RegisterController(BitcornContext dbContext,IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }

        [HttpPost("newuser")]
        public async Task<FullUser> RegisterNewUser([FromBody]Auth0User auth0User)
        {
            if(auth0User == null) throw new ArgumentNullException();

            var existingUserIdentity = await _dbContext.Auth0Query(auth0User.Auth0Id).Select(u=>u.UserIdentity).FirstOrDefaultAsync();
            
            if (existingUserIdentity?.Auth0Id == auth0User.Auth0Id)
            {
                var user = _dbContext.User.FirstOrDefault(u => u.UserId == existingUserIdentity.UserId);
                var userWallet = _dbContext.UserWallet.FirstOrDefault(u => u.UserId == existingUserIdentity.UserId);
                var userStat = _dbContext.UserStat.FirstOrDefault(u => u.UserId == existingUserIdentity.UserId);
                return BitcornUtils.GetFullUser(user, existingUserIdentity, userWallet, userStat);
            }

            try
            {
                var user = new User
                {
                    UserIdentity = new UserIdentity
                    {
                        Auth0Id = auth0User.Auth0Id, Auth0Nickname = auth0User.Auth0Nickname
                    },
                    UserWallet = new UserWallet(),
                    UserStat = new UserStat()
                };
                _dbContext.User.Add(user);
                await _dbContext.SaveAsync();

                return BitcornUtils.GetFullUser(user, user.UserIdentity, user.UserWallet, user.UserStat);
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        FullUser GetFullUser(User user)
        {
            return BitcornUtils.GetFullUser(user, user.UserIdentity, user.UserWallet, user.UserStat);
        }
        async Task MigrateUser(User delete, User user)
        {
            user.Avatar = delete.Avatar;
            user.IsBanned = delete.IsBanned;
            user.Level = delete.Level;
            user.SubTier = delete.SubTier;
         
            user.UserStat.EarnedIdle += delete.UserStat.EarnedIdle;
            user.UserStat.Rained += delete.UserStat.Rained;
            user.UserStat.RainedOn += delete.UserStat.RainedOn;
            user.UserStat.RainedOnTotal += delete.UserStat.RainedOnTotal;
            user.UserStat.RainTotal += delete.UserStat.RainTotal;
            user.UserStat.Tip += delete.UserStat.Tip;
            user.UserStat.Tipped += delete.UserStat.Tipped;
            user.UserStat.TippedTotal += delete.UserStat.TippedTotal;
            user.UserStat.TipTotal += delete.UserStat.TipTotal;
            user.UserStat.TopRain += delete.UserStat.TopRain;
            user.UserStat.TopRainedOn += delete.UserStat.TopRainedOn;
            user.UserStat.TopTip += delete.UserStat.TopTip;
            user.UserStat.TopTipped += delete.UserStat.TopTipped;

            user.UserWallet.Balance += delete.UserWallet.Balance;
            user.UserWallet.CornAddy = delete.UserWallet.CornAddy;
            user.UserWallet.WalletServer = delete.UserWallet.WalletServer;

            _dbContext.Remove(delete.UserWallet);
            _dbContext.Remove(delete.UserIdentity);
            _dbContext.Remove(delete.UserStat);
            _dbContext.User.Remove(delete);
            await _dbContext.Database.ExecuteSqlRawAsync($" UPDATE [{nameof(CornTx)}] SET [{nameof(CornTx.SenderId)}] = {user.UserId} WHERE [{nameof(CornTx.SenderId)}] = {delete.UserId}");
            await _dbContext.Database.ExecuteSqlRawAsync($" UPDATE [{nameof(CornTx)}] SET [{nameof(CornTx.ReceiverId)}] = {user.UserId} WHERE [{nameof(CornTx.ReceiverId)}] = {delete.UserId}");

            await _dbContext.SaveAsync();
        }
        void CopyIdentity(UserIdentity from,UserIdentity to)
        {
            to.Auth0Id = from.Auth0Id;
            to.Auth0Nickname = from.Auth0Nickname;
            to.DiscordId = from.DiscordId;
            to.DiscordUsername = from.DiscordUsername;
            to.RedditId = from.RedditId;
            to.TwitchId = from.TwitchId;
            to.TwitchUsername = from.TwitchUsername;
            to.TwitterId = from.TwitterId;
            to.TwitterUsername = from.TwitterUsername;
        }
        [HttpPost]
        public async Task<FullUser> Register([FromBody] RegistrationData registrationData)
        { 
            if (registrationData == null) throw new ArgumentNullException("registrationData");
            if (registrationData.Auth0Id == null) throw new ArgumentNullException("registrationData.Auth0Id");
            if (registrationData.PlatformId == null) throw new ArgumentNullException("registrationData.PlatformId");

            try
            {
                string auth0Id = registrationData.Auth0Id;
                var auth0DbUser = await _dbContext.Auth0Query(auth0Id).FirstOrDefaultAsync();
                var platformId = BitcornUtils.GetPlatformId(registrationData.PlatformId);
                switch (platformId.Platform)
                {
                    case "twitch":
                        var twitchUser = await TwitchKraken.GetTwitchUser(platformId.Id);

                        var twitchDbUser = await _dbContext.TwitchQuery(platformId.Id).FirstOrDefaultAsync();

                        if (twitchDbUser != null && twitchDbUser.UserIdentity.Auth0Id == null)
                        {
                            //   _dbContext.UserIdentity.Remove(auth0DbUser);
                            auth0DbUser.UserIdentity.TwitchId = twitchDbUser.UserIdentity.TwitchId;
                            CopyIdentity(auth0DbUser.UserIdentity,twitchDbUser.UserIdentity);
                            twitchDbUser.UserIdentity.TwitchUsername = twitchUser.name;
                            twitchDbUser.UserIdentity.Auth0Id = auth0Id;
                            twitchDbUser.UserIdentity.Auth0Nickname = auth0DbUser.UserIdentity.Auth0Nickname;
                           
                            await MigrateUser(auth0DbUser,twitchDbUser);
             

                            await TxUtils.TryClaimTx(platformId, null, _dbContext);
                            return GetFullUser(twitchDbUser);
                        }
                        else if (twitchDbUser == null && auth0DbUser != null)
                        {
                            auth0DbUser.UserIdentity.TwitchId = platformId.Id;
                            auth0DbUser.UserIdentity.TwitchUsername = twitchUser.name;
                            await _dbContext.SaveAsync();

                            await TxUtils.TryClaimTx(platformId, null, _dbContext);
                            return GetFullUser(auth0DbUser);
                        }
                        else if (twitchDbUser != null)
                        {
                            var e = new Exception($"A login id already exists for this twitch id {platformId.Id}");
                            await BITCORNLogger.LogError(_dbContext, e);
                            throw e;
                        }
                        else
                        {
                            var e = new Exception(
                                $"Failed to register twitch {platformId.Id} {auth0Id}");
                            await BITCORNLogger.LogError(_dbContext, e);
                            throw e;
                        }
                    case "discord":
                        try
                        {
                            var discordToken = DiscordApi.GetDiscordBotToken(_configuration);
                            var discordUser = await DiscordApi.GetDiscordUser(discordToken,platformId.Id);

                            var discordDbUser = await _dbContext.DiscordQuery(platformId.Id).FirstOrDefaultAsync();
                            
                            if (discordDbUser != null && discordDbUser.UserIdentity.Auth0Id == null)
                            {
                                //_dbContext.UserIdentity.Remove(auth0DbUser);
                                //await _dbContext.SaveAsync();
                                auth0DbUser.UserIdentity.DiscordId = discordDbUser.UserIdentity.DiscordId;
                                CopyIdentity(auth0DbUser.UserIdentity,discordDbUser.UserIdentity);
                             
                                discordDbUser.UserIdentity.DiscordUsername = DiscordApi.GetUsernameString(discordUser);
                                discordDbUser.UserIdentity.Auth0Id = auth0Id;
                                discordDbUser.UserIdentity.Auth0Nickname = auth0DbUser.UserIdentity.Auth0Nickname;
                                await MigrateUser(auth0DbUser,discordDbUser);
                                await TxUtils.TryClaimTx(platformId, null, _dbContext);
                                return GetFullUser(discordDbUser);
                            }
                            else if (discordDbUser == null && auth0DbUser != null)
                            {
                                auth0DbUser.UserIdentity.DiscordId = platformId.Id;
                                auth0DbUser.UserIdentity.DiscordUsername = DiscordApi.GetUsernameString(discordUser);

                                await _dbContext.SaveAsync();

                                await TxUtils.TryClaimTx(platformId, null, _dbContext);
                                return GetFullUser(auth0DbUser);
                            }
                            else if (discordDbUser?.UserIdentity.Auth0Id != null)
                            {
                                var e = new Exception($"A login id already exists for this discord id");
                                await BITCORNLogger.LogError(_dbContext,e, $"Auth0Id already exists for user {platformId.Id}");
                                throw e;
                            }
                            else
                            {
                                var e = new Exception($"Failed to register discord");
                                await BITCORNLogger.LogError(_dbContext,e, $"Failed to register discord id for user {platformId.Id} {auth0Id}");
                                throw e;
                            }
                        }
                        catch (Exception e)
                        {
                            await BITCORNLogger.LogError(_dbContext,e);
                            throw new Exception($"Failed to add user's discord");
                        }

                        throw new Exception($"HOW THE FUCK DID YOU GET HERE");
                    case "twitter":
                        try
                        {
                            var twitterUser = await TwitterApi.GetTwitterUser(_configuration, platformId.Id);
                            var twitterDbUser = await _dbContext.TwitterQuery(platformId.Id).FirstOrDefaultAsync();

                            if (twitterDbUser != null && twitterDbUser.UserIdentity.Auth0Id == null)
                            {
                                auth0DbUser.UserIdentity.TwitterId = twitterDbUser.UserIdentity.TwitterId;
                                //_dbContext.UserIdentity.Remove(auth0DbUser);
                                CopyIdentity(auth0DbUser.UserIdentity,twitterDbUser.UserIdentity);
                                twitterDbUser.UserIdentity.Auth0Id = auth0Id;
                                twitterDbUser.UserIdentity.TwitterUsername = twitterUser.Name;
                                twitterDbUser.UserIdentity.Auth0Nickname = auth0DbUser.UserIdentity.Auth0Nickname;
                                await MigrateUser(auth0DbUser,twitterDbUser);
                                await TxUtils.TryClaimTx(platformId,null,_dbContext);
                                return GetFullUser(twitterDbUser);
                            }
                            if (twitterDbUser == null && auth0DbUser != null)
                            {
                                auth0DbUser.UserIdentity.TwitterId = platformId.Id;
                                auth0DbUser.UserIdentity.TwitterUsername = twitterUser.Name;
                                await _dbContext.SaveAsync();
                                await TxUtils.TryClaimTx(platformId, null, _dbContext);
                                return GetFullUser(auth0DbUser);
                            }
                            if (twitterDbUser?.UserIdentity.Auth0Id != null)
                            {
                                var e = new Exception($"Auth0Id already exists for user {platformId.Id}");
                                await BITCORNLogger.LogError(_dbContext,e);
                                throw e;
                            }
                            var ex = new Exception($"Failed to register twitter id for user {platformId.Id} {auth0Id}");
                            await BITCORNLogger.LogError(_dbContext,ex);
                            throw ex;
                        }
                        catch (Exception e)
                        {
                            await BITCORNLogger.LogError(_dbContext, e);
                            throw e;
                        }
                        throw new Exception($"HOW THE FUCK DID YOU GET HERE");
                    case "reddit":
                        try
                        {
                            var redditDbUser = await _dbContext.RedditQuery(platformId.Id).FirstOrDefaultAsync();

                            if (redditDbUser != null && redditDbUser.UserIdentity.Auth0Id == null)
                            {
                                auth0DbUser.UserIdentity.RedditId = redditDbUser.UserIdentity.RedditId;
                                CopyIdentity(auth0DbUser.UserIdentity,redditDbUser.UserIdentity);
                                //_dbContext.UserIdentity.Remove(auth0DbUser);
                                redditDbUser.UserIdentity.Auth0Id = auth0Id;
                                redditDbUser.UserIdentity.Auth0Nickname = auth0DbUser.UserIdentity.Auth0Nickname;
                                await MigrateUser(auth0DbUser,redditDbUser);
                                await TxUtils.TryClaimTx(platformId, null, _dbContext);
                                return GetFullUser(redditDbUser);
                            }
                            else if (redditDbUser == null && auth0DbUser != null)
                            {
                                auth0DbUser.UserIdentity.RedditId = platformId.Id;
                                await _dbContext.SaveAsync();

                                await TxUtils.TryClaimTx(platformId, null, _dbContext);
                                return GetFullUser(auth0DbUser);
                            }
                            else if (redditDbUser?.UserIdentity.Auth0Id != null)
                            {
                                var e = new Exception($"Auth0Id already exists for user {platformId.Id}");
                                await BITCORNLogger.LogError(_dbContext, e);
                                throw e;
                            }
                            else
                            {
                                var e = new Exception($"Failed to register reddit id for user {platformId.Id} {platformId.Id}");
                                await BITCORNLogger.LogError(_dbContext, e);
                                throw e;
                            }
                        }
                        catch (Exception e)
                        {
                            await BITCORNLogger.LogError(_dbContext, e);
                            throw e;
                        }

                        throw new Exception($"HOW THE FUCK DID YOU GET HERE");
                    default:
                        throw new Exception("Invalid platform provided in the Id");
                }
            }
            catch(Exception e)
            {
                throw new Exception($"registration failed for {registrationData}");
            }
            throw new Exception("HOW THE FUCK DID YOU GET HERE");
        }

    }

    }
