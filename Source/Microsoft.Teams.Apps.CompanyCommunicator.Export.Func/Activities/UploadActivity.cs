﻿// <copyright file="UploadActivity.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Activities
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading.Tasks;
    using CsvHelper;
    using Microsoft.Azure.Storage;
    using Microsoft.Azure.Storage.Blob;
    using Microsoft.Azure.Storage.RetryPolicies;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.DurableTask;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.NotificationData;
    using Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Model;
    using Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Streams;

    /// <summary>
    /// uploads the file to the blob storage.
    /// </summary>
    public class UploadActivity
    {
        private readonly string storageConnectionString;
        private readonly IDataStreamFacade userDataStream;

        /// <summary>
        /// Initializes a new instance of the <see cref="UploadActivity"/> class.
        /// </summary>
        /// <param name="repositoryOptions">the repository options.</param>
        /// <param name="userDataStream">the user data stream.</param>
        public UploadActivity(
            IOptions<RepositoryOptions> repositoryOptions,
            IDataStreamFacade userDataStream)
        {
            this.storageConnectionString = repositoryOptions.Value.StorageAccountConnectionString;
            this.userDataStream = userDataStream;
        }

        private TimeSpan BackOffPeriod { get; set; } = TimeSpan.FromSeconds(3);

        private int MaxRetry { get; set; } = 15;

        /// <summary>
        /// Run the activity.
        /// Uploads the notification data to Azure Blob Storage.
        /// </summary>
        /// <param name="context">Durable orchestration context.</param>
        /// <param name="uploadData">Tuple containing notification data entity,metadata and filename.</param>
        /// <param name="log">Logging service.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task RunAsync(
        IDurableOrchestrationContext context,
        (NotificationDataEntity sentNotificationDataEntity, MetaData metaData, string fileName) uploadData,
        ILogger log)
        {
            try
            {
                await context.CallActivityWithRetryAsync(
                  nameof(UploadActivity.UploadActivityAsync),
                  ActivitySettings.CommonActivityRetryOptions,
                  uploadData);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to create metadata {ex.Message}";

                log.LogError(ex, errorMessage);
            }
        }

        /// <summary>
        /// Upload the file to azure blob storage.
        /// </summary>
        /// <param name="uploadData">Tuple containing notification data, metadata and filename.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [FunctionName(nameof(UploadActivityAsync))]
        public async Task UploadActivityAsync(
        [ActivityTrigger](NotificationDataEntity sentNotificationDataEntity, MetaData metaData, string fileName) uploadData)
        {
            CloudStorageAccount storage = CloudStorageAccount.Parse(this.storageConnectionString);
            CloudBlobClient client = storage.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(Common.Constants.BlobContainerName);
            CloudBlockBlob blob = container.GetBlockBlobReference(uploadData.fileName);
            var blobRequestOptions = new BlobRequestOptions()
            {
                RetryPolicy = new ExponentialRetry(this.BackOffPeriod, this.MaxRetry),
                SingleBlobUploadThresholdInBytes = 1024 * 1024 * 4, // 4Mb.
                ParallelOperationThreadCount = 1, // Advised to keep 1 if upload size is less than 256 Mb.
            };

            using var memorystream = await blob.OpenWriteAsync(new AccessCondition(), blobRequestOptions, new OperationContext());
            using var archive = new ZipArchive(memorystream, ZipArchiveMode.Create);

            // metadata CSV creation.
            var metadataFile = archive.CreateEntry("Metadata.csv", CompressionLevel.Optimal);
            using (var entryStream = metadataFile.Open())
            using (var writer = new StreamWriter(entryStream))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteHeader(typeof(MetaData));
                await csv.NextRecordAsync();
                csv.WriteRecord(uploadData.metaData);
            }

            // message delivery csv creation.
            var messageDeliveryFile = archive.CreateEntry("Message_Delivery.csv", CompressionLevel.Optimal);
            using (var entryStream = messageDeliveryFile.Open())
            {
                using var writer = new StreamWriter(entryStream);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                if (uploadData.sentNotificationDataEntity.Teams.Count() != 0)
                {
                    var userDataStream = this.userDataStream.GetTeamDataStreamAsync(uploadData.sentNotificationDataEntity.Id);
                    await foreach (var data in userDataStream)
                    {
                        await csv.WriteRecordsAsync(data);
                    }
                }
                else
                {
                    var teamDataStream = this.userDataStream.GetUserDataStreamAsync(uploadData.sentNotificationDataEntity.Id);
                    await foreach (var data in teamDataStream)
                    {
                        await csv.WriteRecordsAsync(data);
                    }
                }
            }
        }
    }
}
