using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using System.IO;
using System.Net.Mail;
using System.Net;

namespace emailSenderFunction
{
    public class Function1
    {
        private readonly ILogger _logger;
        private IConfiguration configuration;

        public Function1(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<Function1>();

            configuration = new ConfigurationBuilder().AddUserSecrets<Function1>().Build();
        }

        [Function("Function1")]
        public async Task Run([BlobTrigger("docx-container/{name}", Connection = "settingConnection")] string myBlob, string name)
        {
            _logger.LogInformation($"C# Blob trigger function Processed blob\n Name: {name} \n Data: {myBlob}");

            string? accountName = configuration.GetSection("azureCredentials")["accountName"];
            string? accountKey = configuration.GetSection("azureCredentials")["accountKey"];
            string? containerName = configuration.GetSection("azureInfo")["containerName"];

            var blobServiceClient = new BlobServiceClient(configuration["settingConnection"]);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(configuration.GetSection("azureInfo")["containerName"]);
            var blobClient = blobContainerClient.GetBlobClient(name);
            BlobProperties blobProperties = await blobClient.GetPropertiesAsync();

            if (!blobProperties.Metadata.ContainsKey("Email"))
                return;        

            var blobSasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = containerName,
                BlobName = name,
                Resource = "b",
                ExpiresOn = DateTime.Now.AddHours(1)
            };
            blobSasBuilder.SetPermissions(BlobAccountSasPermissions.Read);

            var storageSharedKeyCredential = new StorageSharedKeyCredential(
                accountName,
                accountKey);

            var sasQueryParameters = blobSasBuilder.ToSasQueryParameters(storageSharedKeyCredential);
            UriBuilder fileSasUri = new UriBuilder($"https://{accountName}.blob.core.windows.net/{blobSasBuilder.BlobContainerName}/{blobSasBuilder.BlobName}");
            fileSasUri.Query = sasQueryParameters.ToString();

            string email = blobProperties.Metadata["Email"];

            SendEmail(email, fileSasUri.Uri.AbsoluteUri.ToString());

            return;         
        }

        private void SendEmail(string email, string uri)
        {
            string? fromEmail = configuration.GetSection("gmailCredentials")["accountEmail"];
            string? fromPassword = configuration.GetSection("gmailCredentials")["accountPassword"];

            MailAddress fromAddress = new MailAddress(fromEmail);

            SmtpClient smtpClient = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword),
                EnableSsl = true
            };

            MailMessage mailMessage = new MailMessage()
            {
                Subject = "Your uploaded file (1 hour access)",
                Body = uri,
                IsBodyHtml = false,
                From = fromAddress,
            };
            mailMessage.To.Add(email);

            smtpClient.Send(mailMessage);
        }
    }
}
