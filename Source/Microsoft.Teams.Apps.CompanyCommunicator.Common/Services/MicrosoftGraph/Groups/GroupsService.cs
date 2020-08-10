﻿// <copyright file="GroupsService.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Common.Services.MicrosoftGraph.Groups
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Graph;

    /// <summary>
    /// Groups Service.
    /// </summary>
    public class GroupsService : IGroupsService
    {
        private readonly IGraphServiceClient graphServiceClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="GroupsService"/> class.
        /// </summary>
        /// <param name="graphServiceClient">graph service client.</param>
        public GroupsService(IGraphServiceClient graphServiceClient)
        {
            this.graphServiceClient = graphServiceClient;
        }

        private int MaxResultCount { get; set; } = 25;

        private int MaxRetry { get; set; } = 2;

        /// <summary>
        /// get groups by ids.
        /// </summary>
        /// <param name="groupIds">list of group ids.</param>
        /// <returns>list of groups.</returns>
        public async IAsyncEnumerable<Group> GetByIdsAsync(IEnumerable<string> groupIds)
        {
            foreach (var id in groupIds)
            {
                var group = await this.graphServiceClient
                                .Groups[id]
                                .Request()
                                .WithMaxRetry(this.MaxRetry)
                                .Select(gr => new { gr.Id, gr.Mail, gr.DisplayName, gr.Visibility, })
                                .Header(Common.Constants.PermissionTypeKey, GraphPermissionType.Delegate.ToString())
                                .GetAsync();
                yield return group;
            }
        }

        /// <summary>
        /// check if list has hidden membership group.
        /// </summary>
        /// <param name="groupIds">list of group ids.</param>
        /// <returns>boolean.</returns>
        public async Task<bool> ContainsHiddenMembershipAsync(IEnumerable<string> groupIds)
        {
            var groups = this.GetByIdsAsync(groupIds);
            await foreach (var group in groups)
            {
                if (group.Visibility.IsHiddenMembership())
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Search M365 groups,distribution groups, security groups based on query.
        /// </summary>
        /// <param name="query">query param.</param>
        /// <returns>list of group.</returns>
        public async Task<IList<Group>> SearchAsync(string query)
        {
            string filterforM365 = Uri.EscapeUriString($"groupTypes/any(c:c+eq+'Unified') and mailEnabled eq true and (startsWith(mail,'{query}') or startsWith(displayName,'{query}'))");
            var groupList = await this.SearchAsync(filterforM365, Common.Constants.HiddenMembership, this.MaxResultCount);
            groupList.AddRange(await this.AddDistributionGroupAsync(query, this.MaxResultCount - groupList.Count()));
            groupList.AddRange(await this.AddSecurityGroupAsync(query, this.MaxResultCount - groupList.Count()));
            return groupList;
        }

        /// <summary>
        /// Search Distribution Groups based on query.
        /// </summary>
        /// <param name="query">query param.</param>
        /// <param name="resultCount">total page size.</param>
        /// <returns>list of distribution group.</returns>
        private async Task<IEnumerable<Group>> AddDistributionGroupAsync(string query, int resultCount)
        {
            if (resultCount == 0)
            {
                return default;
            }

            string filterforDL = Uri.EscapeUriString($"mailEnabled eq true and (startsWith(mail,'{query}') or startsWith(displayName,'{query}'))");
            var distributionGroups = await this.SearchAsync(filterforDL, resultCount);

            // Filtering the result only for distribution groups.
            var distributionGroupList = distributionGroups.CurrentPage.
                                                           Where(dg => dg.GroupTypes == null).ToList();
            while (distributionGroups.NextPageRequest != null && distributionGroupList.Count() < resultCount)
            {
                distributionGroups = await distributionGroups.NextPageRequest.GetAsync();
                distributionGroupList.AddRange(distributionGroups.CurrentPage.Where(dg => dg.GroupTypes == null));
            }

            return distributionGroupList.Take(resultCount);
        }

        /// <summary>
        /// Search Security Groups based on query.
        /// </summary>
        /// <param name="query">query param.</param>
        /// <param name="resultCount">total page size.</param>
        /// <returns>list of security group.</returns>
        private async Task<IEnumerable<Group>> AddSecurityGroupAsync(string query, int resultCount)
        {
            if (resultCount == 0)
            {
                return default;
            }

            string filterforSG = Uri.EscapeUriString($"mailEnabled eq false and securityEnabled eq true and startsWith(displayName,'{query}')");
            var sgGroups = await this.SearchAsync(filterforSG, resultCount);
            return sgGroups.CurrentPage.Take(resultCount);
        }

        /// <summary>
        /// Search M365 groups, distribution groups, security groups based on query and visibilty.
        /// </summary>
        /// <param name="filterQuery">query param.</param>
        /// <param name="visibility">remove hidden membership.</param>
        /// <param name="resultCount">page size.</param>
        /// <returns>list of group.</returns>
        private async Task<List<Group>> SearchAsync(string filterQuery, string visibility, int resultCount)
        {
            var groupsPaged = await this.SearchAsync(filterQuery, resultCount);
            if (string.IsNullOrEmpty(visibility))
            {
                return groupsPaged.CurrentPage.ToList();
            }

            var groupList = groupsPaged.CurrentPage.
                                        Where(group => !group.Visibility.IsHiddenMembership()).
                                        ToList();
            while (groupsPaged.NextPageRequest != null && groupList.Count() < resultCount)
            {
                groupsPaged = await groupsPaged.NextPageRequest.GetAsync();
                groupList.AddRange(groupsPaged.CurrentPage.
                          Where(group => !group.Visibility.IsHiddenMembership()));
            }

            return groupList.Take(resultCount).ToList();
        }

        /// <summary>
        /// Search M365 groups,sistribution groups, security groups based on query.
        /// </summary>
        /// <param name="filterQuery">query param.</param>
        /// <param name="resultCount">page size.</param>
        /// <returns>graph group collection.</returns>
        private async Task<IGraphServiceGroupsCollectionPage> SearchAsync(string filterQuery, int resultCount)
        {
            return await this.graphServiceClient
                                   .Groups
                                   .Request()
                                   .WithMaxRetry(this.MaxRetry)
                                   .Filter(filterQuery)
                                   .Select(group => new
                                   {
                                       group.Id,
                                       group.Mail,
                                       group.DisplayName,
                                       group.Visibility,
                                       group.GroupTypes,
                                   }).
                                   Top(resultCount)
                                   .Header(Common.Constants.PermissionTypeKey, GraphPermissionType.Delegate.ToString())
                                   .GetAsync();
        }
    }
}