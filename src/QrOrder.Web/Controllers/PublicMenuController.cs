using Microsoft.AspNetCore.Mvc;
using QrOrder.Application.Public;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("public/menu")]
    public class PublicMenuController : ControllerBase
    {
        private readonly IPublicMenuService _menuService;

        public PublicMenuController(IPublicMenuService menuService)
        {
            _menuService = menuService;
        }

        [HttpGet("{tenantSlug}")]
        public async Task<IActionResult> Get(string tenantSlug)
        {
            var menu = await _menuService.GetMenuAsync(tenantSlug);
            return menu == null ? NotFound("Tenant not found.") : Ok(menu);
        }
    }
}
