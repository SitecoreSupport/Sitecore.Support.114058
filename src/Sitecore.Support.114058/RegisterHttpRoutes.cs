namespace Sitecore.Support.ListManagement.Services.Pipelines.Initialize
{
  using Sitecore.ListManagement.Services.Pipelines.Initialize;
  using System;
  using System.Web.Http;

  public class RegisterHttpRoutes : HttpConfigurationProcessor
  {
    protected override void Configure(HttpConfiguration configuration)
    {
      HttpRouteCollection routes = configuration.Routes;
      routes.MapHttpRoute("ListManagement_Actions", "sitecore/api/ListManagement/Actions/{action}/{listId}", new
      {
        controller = "Actions",
        action = "Index",
        listId = RouteParameter.Optional,
      },
      new[] { "Sitecore.Support.114058" });

      routes.MapHttpRoute("ListManagement_Import", "sitecore/api/ListManagement/Import/{action}", new
      {
        controller = "Import",
        action = "Index"
      });
    }
  }
}