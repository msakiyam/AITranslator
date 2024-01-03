using System;
using System.Threading.Tasks;
using Azure.AI.Translation.Document;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

class Program
{
    static async Task DeleteAllBlobsInContainerAsync(BlobContainerClient containerClient)
    {
        // Iterate through all the blobs in the container
        await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
        {
            // Get a BlobClient for the blob
            BlobClient blobClient = containerClient.GetBlobClient(blobItem.Name);

            // Delete the blob
            await blobClient.DeleteIfExistsAsync();
            Console.WriteLine($"Deleted blob {blobItem.Name}");
        }
    }
    static async Task Main(string[] args)
    {
        string fileName = "";
        string SourceDocumentPath = "C:\\temp\\AITranslator\\";
        string fullPath = Path.Combine(SourceDocumentPath, fileName);


        // Endpoint and key for your Document Translation resource
        string endpoint = "";
        string key = "";

        string BlobStorageName = "";
        string BlobStorageKey = "";
        string blobUri = "";

    
        string sourceContainerName = "test-src";
        string targetContainerName = "test-tgt";

        StorageSharedKeyCredential sharedKeyCredential = new(BlobStorageName, BlobStorageKey);
        BlobServiceClient blobServiceClient = new(new Uri(blobUri), sharedKeyCredential);
        var sourceContainerClient = blobServiceClient.GetBlobContainerClient(sourceContainerName);
        if (!sourceContainerClient.Exists())
        {
            sourceContainerClient = await blobServiceClient.CreateBlobContainerAsync(sourceContainerName, PublicAccessType.None);
        }
        var targetContainerClient = blobServiceClient.GetBlobContainerClient(targetContainerName);
        if (!targetContainerClient.Exists())
        {
            targetContainerClient = await blobServiceClient.CreateBlobContainerAsync(targetContainerName, PublicAccessType.None);
        }

        // Delete all blobs in the source container
        await DeleteAllBlobsInContainerAsync(sourceContainerClient);

        // Delete all blobs in the target container
        await DeleteAllBlobsInContainerAsync(targetContainerClient);

        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"Error: The file '{fullPath}' does not exist.");
            return;
        }

        BlobClient srcBlobClient = sourceContainerClient.GetBlobClient(fileName);

        using (FileStream uploadFileStream = File.OpenRead(fullPath))
        {
            await srcBlobClient.UploadAsync(uploadFileStream, true).ConfigureAwait(false);
        }

        Uri sourceUri = sourceContainerClient.GenerateSasUri(BlobContainerSasPermissions.List | BlobContainerSasPermissions.Read, DateTime.UtcNow.AddMinutes(60));
        Uri targetUri = targetContainerClient.GenerateSasUri(BlobContainerSasPermissions.All | BlobContainerSasPermissions.List | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.Delete, DateTime.UtcNow.AddMinutes(60));

        Console.WriteLine($"  sourceSASUri: {sourceUri}");
        Console.WriteLine($"  targetSASUri: {targetUri}");

        var client = new DocumentTranslationClient(new Uri(endpoint), new Azure.AzureKeyCredential(key));

        var input = new DocumentTranslationInput(sourceUri, targetUri, "es");

        DocumentTranslationOperation operation = await client.StartTranslationAsync(input);

        await operation.WaitForCompletionAsync();

        Console.WriteLine($"  Status: {operation.Status}");
        Console.WriteLine($"  Created on: {operation.CreatedOn}");
        Console.WriteLine($"  Last modified: {operation.LastModified}");
        Console.WriteLine($"  Total documents: {operation.DocumentsTotal}");
        Console.WriteLine($"    Succeeded: {operation.DocumentsSucceeded}");
        Console.WriteLine($"    Failed: {operation.DocumentsFailed}");
        Console.WriteLine($"    In Progress: {operation.DocumentsInProgress}");
        Console.WriteLine($"    Not started: {operation.DocumentsNotStarted}");

        await foreach (DocumentStatusResult document in operation.Value)
        {
            Console.WriteLine($"Document with Id: {document.Id}");
            Console.WriteLine($"  Status:{document.Status}");
            if (document.Status == DocumentTranslationStatus.Succeeded)
            {
                Console.WriteLine($"  Translated Document Uri: {document.TranslatedDocumentUri}");
                Console.WriteLine($"  Translated to language code: {document.TranslatedToLanguageCode}.");
                Console.WriteLine($"  Document source Uri: {document.SourceDocumentUri}");
            }
            else if (document.Error != null)
            {
                Console.WriteLine($"  Error Code: {document.Error.Code}");
                Console.WriteLine($"  Message: {document.Error.Message}");
            }
        }

        //download the translated file
        // List blobs in the target container
        await foreach (BlobItem blobItem in targetContainerClient.GetBlobsAsync())
        {
            // Get a reference to the blob
            BlobClient blobClient = targetContainerClient.GetBlobClient(blobItem.Name);

            // Build the local file path where you want to download the file
            string downloadFilePath = Path.Combine(SourceDocumentPath, blobItem.Name);

            // Download the blob to a local file
            BlobDownloadInfo download = await blobClient.DownloadAsync();

            // Write the file to the local path
            using (FileStream downloadFileStream = File.OpenWrite(downloadFilePath))
            {
                await download.Content.CopyToAsync(downloadFileStream);
                downloadFileStream.Close();
            }

            Console.WriteLine($"Downloaded blob to {downloadFilePath}");

            // Assuming you only expect one file for simplicity; remove this if you loop through all files
            //break;

        }
    }

}