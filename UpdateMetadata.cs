using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using System.Linq;
using System.Xml;
using System.Text;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.WindowsAzure.Storage;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;

namespace MetadataExplorer
{
    public static class UpdateMetadata
    {
        static Dictionary<string, string> metadataMap = new Dictionary<string, string>{
            { "v1", "https://graph.microsoft.com/v1.0/$metadata" },
            { "stagingv1", "https://graph.microsoft.com/stagingv1.0/$metadata" },
            { "cleanv1", "https://raw.githubusercontent.com/microsoftgraph/msgraph-metadata/master/clean_v10_metadata/cleanMetadataWithDescriptionsv1.0.xml" },
            { "beta", "https://graph.microsoft.com/beta/$metadata" },
            { "stagingbeta", "https://graph.microsoft.com/stagingbeta/$metadata" },
            { "cleanbeta", "https://raw.githubusercontent.com/microsoftgraph/msgraph-metadata/master/clean_beta_metadata/cleanMetadataWithDescriptionsbeta.xml" },
            };

        [FunctionName("UpdateMetadata")]
        public static void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger log, ExecutionContext context)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            foreach (KeyValuePair<string, string> metadata in metadataMap)
            {
                RefreshCache(metadata.Key, metadata.Value, context);
            }
        }

        [FunctionName("UpdateMetadataHttp")]
        public static IActionResult UpdateMetadataHttp([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]HttpRequest req, ILogger log, ExecutionContext context)
        {
            // TODO
            foreach (KeyValuePair<string, string> metadata in metadataMap)
            {
                RefreshCache(metadata.Key, metadata.Value, context);
            }

            return new OkResult();
        }

        [FunctionName("UpdateStaticFilesHttp")]
        public static async Task<IActionResult> UpdateStaticFilesHttp([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context)
        {
            await RefreshStaticFiles(context);
            return new OkResult();
        }

        private static async Task RefreshStaticFiles(ExecutionContext context)
        {
            const string cssFileName = "main.css";
            const string jsFileName = "main.js";
            var staticDirectory = Path.Combine(context.FunctionAppDirectory, "static");
            var template = File.ReadAllText(Path.Combine(staticDirectory, "template.html"));

            var storageAccount = GetCloudStorageAccount(context);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("$web");

            foreach (KeyValuePair<string, string> metadata in metadataMap)
            {
                await container.UploadTextAsync($"{metadata.Key}.html", string.Format(template, metadata.Value, metadata.Key), "html");
            }

            await container.UploadTextAsync(cssFileName, File.ReadAllText(Path.Combine(staticDirectory, cssFileName)), "css");
            await container.UploadTextAsync(jsFileName, File.ReadAllText(Path.Combine(staticDirectory, jsFileName)), "javascript");
        }

        private static Task UploadTextAsync(this CloudBlobContainer container, string fileName, string fileContents, string contentType)
        {
            var blob = container.GetBlockBlobReference(fileName);
            blob.Properties.ContentType = $"text/{contentType}";
            return blob.UploadTextAsync(fileContents);
        }

        private static void RefreshCache(string version, string metadataUrl, ExecutionContext context)
        {
            var types = new List<GraphType>();

            var xmlReader = XmlReader.Create(metadataUrl);
            var edmModel = CsdlReader.Parse(xmlReader);

            foreach (var entitySet in edmModel.EntityContainer.EntitySets())
            {
                types.Add(new EntitySet
                {
                    Name = entitySet.Name,
                    FullName = entitySet.Type.FullTypeName(),
                });
            }

            foreach (var singleton in edmModel.EntityContainer.Singletons())
            {
                types.Add(new EntitySet
                {
                    Name = singleton.Name,
                    FullName = singleton.Type.FullTypeName(),
                });
            }

            foreach (var e in edmModel.SchemaElements.Where(e => e is IEdmStructuredType))
            {
                var element = e as IEdmStructuredType;
                var fullName = element.FullTypeName();
                types.Add(element.TypeKind switch
                {
                    EdmTypeKind.Entity => new EntityType
                    {
                        BaseType = element.BaseType?.FullTypeName(),
                        FullName = fullName,
                        Name = ShortenTypeName(fullName),
                        IsAbstract = element.IsAbstract,
                        Properties = element.Properties().Where(x => !(x is IEdmNavigationProperty)).Select(x => new Property
                        {
                            Class = fullName,
                            Name = x.Name,
                            Type = x.Type.FullName(),
                        }).Union(
                            element.NavigationProperties().Select(x => new NavProperty
                            {
                                Class = fullName,
                                Name = x.Name,
                                Type = x.Type.FullName(),
                                ContainsTarget = x.ContainsTarget,
                                IsCollection = x.Type.IsCollection()
                            })).ToArray()
                    },
                    EdmTypeKind.Complex => new ComplexType
                    {
                        BaseType = element.BaseType?.FullTypeName(),
                        FullName = fullName,
                        Name = ShortenTypeName(fullName),
                        Properties = element.Properties().Select(x => new Property
                        {
                            Class = fullName,
                            Name = x.Name,
                            Type = x.Type.FullName(),
                        }).ToArray()
                    },
                    _ => throw new NotSupportedException()
                });
            }

            foreach (var e in edmModel.SchemaElements.Where(e => e is IEdmEnumType))
            {
                var fullName = e.FullName();
                types.Add(new EnumType
                {
                    FullName = fullName,
                    Name = ShortenTypeName(fullName),
                    Members = (e as IEdmEnumType).Members.Select(x => x.Name).ToArray()
                });
            }

            var sb = new StringBuilder();
            sb.Append("const json = [");
            foreach (var type in types)
            {
                sb.Append("{");
                AppendProperty(sb, "ItemType", type.ItemType);
                AppendProperty(sb, "Name", type.Name);
                AppendProperty(sb, "Css", type.CssClass);
                switch (type)
                {
                    case EntityType e:
                        if (e.BaseType is object)
                        {
                            AppendProperty(sb, "BaseType", ShortenTypeName(e.BaseType));
                        }
                        AppendPropertyBool(sb, "IsAbstract", e.IsAbstract);
                        AppendProperties(sb, e.Properties);
                        break;

                    case ComplexType c:
                        if (c.BaseType is object)
                        {
                            AppendProperty(sb, "BaseType", ShortenTypeName(c.BaseType));
                        }
                        AppendProperties(sb, c.Properties);
                        break;
                    case EnumType e:
                        sb.Append(@"""Members"" : [");
                        foreach (var member in e.Members)
                        {
                            sb.Append($@"""{member}"",");
                        }
                        sb.Remove(sb.Length - 1, 1);
                        sb.Append("]");
                        break;
                    case EntitySet e:
                        AppendProperty(sb, "Type", ShortenTypeName(e.FullName));
                        break;
                    case Singleton s:
                        AppendProperty(sb, "Type", ShortenTypeName(s.FullName));
                        break;
                    default:
                        sb.Remove(sb.Length - 1, 1);
                        break;
                }
                sb.Append("},");
            }
            sb.Remove(sb.Length - 1, 1);
            sb.AppendLine("];");

            var storageAccount = GetCloudStorageAccount(context);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("$web");

            var blob = container.GetBlockBlobReference($"{version}.js");
            blob.UploadTextAsync(sb.ToString());
        }

        private static CloudStorageAccount GetCloudStorageAccount(ExecutionContext executionContext)
        {
            var config = new ConfigurationBuilder()
                            .SetBasePath(executionContext.FunctionAppDirectory)
                            .AddJsonFile("local.settings.json", true, true)
                            .AddEnvironmentVariables().Build();
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config["AzureWebJobsStorage"]);
            return storageAccount;
        }

        private static void AppendProperty(StringBuilder sb, string name, string value)
        {
            sb.Append($@"""{name}"" : ""{value}"",");
        }

        private static void AppendPropertyBool(StringBuilder sb, string name, bool value)
        {
            int intValue = value ? 1 : 0;
            sb.Append($@"""{name}"" :  {intValue},");
        }

        private static void AppendProperties(StringBuilder sb, Property[] properties)
        {
            sb.Append(@"""Properties"" : [");
            foreach (var property in properties)
            {
                sb.Append("{");
                AppendProperty(sb, "Name", property.Name);
                AppendProperty(sb, "Type", ShortenTypeName(property.Type));
                if (property is NavProperty np)
                {
                    AppendProperty(sb, "ContainsTarget", np.ContainsTarget.ToString());
                    AppendProperty(sb, "IsCollection", np.IsCollection.ToString());
                }
                sb.Remove(sb.Length - 1, 1);
                sb.Append("},");
            }

            if (properties.Length > 0)
            {
                sb.Remove(sb.Length - 1, 1);
            }

            sb.Append("]");
        }

        private static string ShortenTypeName(string typeName)
        {
            return typeName
                .Replace("microsoft.graph.", "")
                .Replace("Collection(", "C(");
        }
    }
}
