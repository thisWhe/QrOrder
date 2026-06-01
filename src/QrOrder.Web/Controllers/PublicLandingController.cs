/*using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace QrOrder.Web.Controllers
{// Bu endpoint şimdilik sadece yönlendirme/kolaylık için.
 // Gerçek UI (mobile web) daha sonra eklenecek.
 //[Route("api/[controller]")]
    [ApiController]
    public class PublicLandingController : ControllerBase
    {
        [HttpGet("/debug/v/{tenantSlug}/t/{tableCode}")]
        public IActionResult Landing(string tenantSlug, string tableCode)
        {
            // Şimdilik kullanıcıya hızlı test linki veriyoruz:
            // 1) Menü: /public/menu/{tenantSlug}
            // 2) Session: POST /public/sessions (tenantSlug, tableCode)
            return Content(
                $"Tenant: {tenantSlug}\nTableCode: {tableCode}\n\n" +
                $"Menu: /public/menu/{tenantSlug}\n" +
                $"Create Session: POST /public/sessions {{ tenantSlug, tableCode }}",
                "text/plain");
        }
    }
}

//  Gerçek üründe burası mobile UI olur (menü + sepet + sipariş). Ama API’yi oturtmak için bu yeterli.
*/