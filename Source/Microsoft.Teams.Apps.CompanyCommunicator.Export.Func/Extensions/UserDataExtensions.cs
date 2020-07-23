﻿// <copyright file="UserDataExtensions.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Extensions
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Graph;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.SentNotificationData;
    using Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Model;

    /// <summary>
    /// Extensions for user data.
    /// </summary>
    public static class UserDataExtensions
    {
        /// <summary>
        /// Create user data.
        /// </summary>
        /// <param name="sentNotificationDataEntities">the list of sent notification data entities.</param>
        /// <param name="users">the user list.</param>
        /// <returns>list of created user data.</returns>
        public static IEnumerable<UserData> CreateUserData(
        this IEnumerable<SentNotificationDataEntity> sentNotificationDataEntities,
        IEnumerable<User> users)
        {
            var userdatalist = new List<UserData>();
            foreach (var sentNotification in sentNotificationDataEntities)
            {
                var user = users.
                Where(user => user != null && user.Id.Equals(sentNotification.RowKey))
                .FirstOrDefault();

                var userData = new UserData
                {
                    Id = sentNotification.RowKey,
                    Name = user == null ? Common.Constants.AdminConsentError : user.DisplayName,
                    Upn = user == null ? Common.Constants.AdminConsentError : user.UserPrincipalName,
                    DeliveryStatus = sentNotification.DeliveryStatus,
                    StatusReason = sentNotification.ErrorMessage.AddStatusCode(
                        sentNotification.StatusCode.ToString()),
                };
                userdatalist.Add(userData);
            }

            return userdatalist;
        }
    }
}
