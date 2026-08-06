using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(KpiReport.Web.Startup))]
namespace KpiReport.Web
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
