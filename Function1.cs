using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Communication.Email;
using Azure.Security.KeyVault.Secrets;
using Azure.Core;
using Azure.Identity;

namespace emailSenderFunction
{
    public class Function1
    {
        private readonly ILogger _logger;
        private IConfiguration configuration;
        private SecretClient secretClient;

        public Function1(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<Function1>();
            this.configuration = configuration;
            SecretClientOptions options = new SecretClientOptions()
            {
                Retry =
                {
                    Delay= TimeSpan.FromSeconds(2),
                    MaxDelay = TimeSpan.FromSeconds(16),
                    MaxRetries = 5,
                    Mode = RetryMode.Exponential
                }
            };

            secretClient = new SecretClient(new Uri(configuration["AzureKeyValutUrl"]), new DefaultAzureCredential(), options);
        }

        [Function("Function1")]
        public async Task Run([BlobTrigger("docx-container/{name}", Connection = "settingConnection")] string myBlob, string name)
        {
            _logger.LogInformation($"C# Blob trigger function Processed blob\n Name: {name} \n Data: {myBlob}");

            var accountName = await secretClient.GetSecretAsync("azureAccountName");
            var accountKey = await secretClient.GetSecretAsync("azureAccountKey");
            var containerName = await secretClient.GetSecretAsync("azureContainerName");

            var blobServiceClient = new BlobServiceClient(configuration["settingConnection"]);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName.Value.Value);
            var blobClient = blobContainerClient.GetBlobClient(name);
            BlobProperties blobProperties = await blobClient.GetPropertiesAsync();

            if (!blobProperties.Metadata.ContainsKey("Email"))
                return;        

            var blobSasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = containerName.Value.Value,
                BlobName = name,
                Resource = "b",
                ExpiresOn = DateTime.Now.AddHours(1)
            };
            blobSasBuilder.SetPermissions(BlobAccountSasPermissions.Read);

            var storageSharedKeyCredential = new StorageSharedKeyCredential(
                accountName.Value.Value,
                accountKey.Value.Value);

            var sasQueryParameters = blobSasBuilder.ToSasQueryParameters(storageSharedKeyCredential);
            UriBuilder fileSasUri = new UriBuilder($"https://{accountName.Value.Value}.blob.core.windows.net/{blobSasBuilder.BlobContainerName}/{blobSasBuilder.BlobName}");
            fileSasUri.Query = sasQueryParameters.ToString();

            string email = blobProperties.Metadata["Email"];

            SendEmail(email, fileSasUri.Uri.AbsoluteUri.ToString());

            return;         
        }

        private async Task SendEmail(string email, string uri)
        {
            var connectionString = await secretClient.GetSecretAsync("emailConnectionString");
            var fromEmail = await secretClient.GetSecretAsync("emailFrom");
          
            var emailClient = new EmailClient(connectionString.Value.Value);

            try
            {
                var emailSendOperation = await emailClient.SendAsync(
                    Azure.WaitUntil.Completed,
                    senderAddress: fromEmail.Value.Value,
                    recipientAddress: email,
                    subject: "Your uploaded file (1 hour access)",
                    htmlContent: uri);
            } catch (Exception ex)
            {
                _logger.LogInformation(ex, ex.Message);
            }
        }
    }
}
