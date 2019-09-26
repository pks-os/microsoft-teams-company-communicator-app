﻿// <copyright file="SentNotificationsController.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Teams.Apps.CompanyCommunicator.Authentication;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.NotificationData;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.TeamData;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Services.MessageQueue;
    using Microsoft.Teams.Apps.CompanyCommunicator.Models;
    using Newtonsoft.Json;

    /// <summary>
    /// Controller for the sent notification data.
    /// </summary>
    [Authorize(PolicyNames.MustBeValidUpnPolicy)]
    [Route("api/sentNotifications")]
    public class SentNotificationsController : ControllerBase
    {
        private readonly NotificationDataRepository notificationDataRepository;
        private readonly TeamDataRepository teamDataRepository;
        private readonly PrepareToSendQueue prepareToSendQueue;

        /// <summary>
        /// Initializes a new instance of the <see cref="SentNotificationsController"/> class.
        /// </summary>
        /// <param name="notificationDataRepository">Notification data repository service that deals with the table storage in azure.</param>
        /// <param name="teamDataRepository">Team data repository instance.</param>
        /// <param name="prepareToSendQueue">The service bus queue for preparing to send notifications.</param>
        public SentNotificationsController(
            NotificationDataRepository notificationDataRepository,
            TeamDataRepository teamDataRepository,
            PrepareToSendQueue prepareToSendQueue)
        {
            this.notificationDataRepository = notificationDataRepository;
            this.teamDataRepository = teamDataRepository;
            this.prepareToSendQueue = prepareToSendQueue;
        }

        /// <summary>
        /// Send a notification, which turns a draft to be a sent notification.
        /// </summary>
        /// <param name="draftNotification">An instance of <see cref="DraftNotification"/> class.</param>
        /// <returns>The result of an action method.</returns>
        [HttpPost]
        public async Task<IActionResult> CreateSentNotificationAsync(
            [FromBody]DraftNotification draftNotification)
        {
            var draftNotificationDataEntity = await this.notificationDataRepository.GetAsync(
                PartitionKeyNames.NotificationDataTable.DraftNotificationsPartition,
                draftNotification.Id);
            if (draftNotificationDataEntity == null)
            {
                return this.NotFound();
            }

            var notificationDataEntityId =
                await this.notificationDataRepository.MoveDraftToSentPartitionAsync(draftNotificationDataEntity);

            var serializedDraftNotificationId = JsonConvert.SerializeObject(notificationDataEntityId);
            var message = new Message(Encoding.UTF8.GetBytes(serializedDraftNotificationId));
            message.ScheduledEnqueueTimeUtc = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            await this.prepareToSendQueue.SendAsync(message);

            return this.Ok();
        }

        /// <summary>
        /// Get most recently sent notification summaries.
        /// </summary>
        /// <returns>A list of <see cref="SentNotificationSummary"/> instances.</returns>
        [HttpGet]
        public async Task<IEnumerable<SentNotificationSummary>> GetSentNotificationsAsync()
        {
            var notificationEntities = await this.notificationDataRepository.GetMostRecentSentNotificationsAsync();

            var result = new List<SentNotificationSummary>();
            foreach (var notificationEntity in notificationEntities)
            {
                var summary = new SentNotificationSummary
                {
                    Id = notificationEntity.Id,
                    Title = notificationEntity.Title,
                    CreatedDateTime = notificationEntity.CreatedDate,
                    SentDate = notificationEntity.SentDate,
                    Succeeded = notificationEntity.Succeeded,
                    Failed = notificationEntity.Failed,
                    Throttled = notificationEntity.Throttled,
                    TotalMessageCount = notificationEntity.TotalMessageCount,
                    IsCompleted = notificationEntity.IsCompleted,
                    SendingStartedDate = notificationEntity.SendingStartedDate,
                };

                result.Add(summary);
            }

            return result;
        }

        /// <summary>
        /// Get a sent notification by Id.
        /// </summary>
        /// <param name="id">Id of the requested sent notification.</param>
        /// <returns>Required sent notification.</returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSentNotificationByIdAsync(string id)
        {
            var notificationEntity = await this.notificationDataRepository.GetAsync(
                PartitionKeyNames.NotificationDataTable.SentNotificationsPartition,
                id);
            if (notificationEntity == null)
            {
                return this.NotFound();
            }

            var result = new SentNotification
            {
                Id = notificationEntity.Id,
                Title = notificationEntity.Title,
                ImageLink = notificationEntity.ImageLink,
                Summary = notificationEntity.Summary,
                Author = notificationEntity.Author,
                ButtonTitle = notificationEntity.ButtonTitle,
                ButtonLink = notificationEntity.ButtonLink,
                CreatedDateTime = notificationEntity.CreatedDate,
                SentDate = notificationEntity.SentDate,
                Succeeded = notificationEntity.Succeeded,
                Failed = notificationEntity.Failed,
                Throttled = notificationEntity.Throttled,
                TeamNames = await this.teamDataRepository.GetTeamNamesByIdsAsync(notificationEntity.Teams),
                RosterNames = await this.teamDataRepository.GetTeamNamesByIdsAsync(notificationEntity.Rosters),
                AllUsers = notificationEntity.AllUsers,
                SendingStartedDate = notificationEntity.SendingStartedDate,
                ErrorMessage = notificationEntity.ExceptionMessage,
                WarningMessage = notificationEntity.WarningMessage,
            };

            return this.Ok(result);
        }
    }
}
