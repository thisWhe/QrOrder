using QrOrder.Domain.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Domain.Entities
{
    public class Product : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }

        public Guid CategoryId { get; set; }
        public Category Category { get; set; } = default!;

        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public string? Ingredients { get; set; }
        public string? PortionInfo { get; set; }
        public int? Calories { get; set; }
        public QrOrder.Domain.Enums.AllergenFlags AllergenFlags { get; set; }
        public bool ContainsAlcohol { get; set; }
        public bool ContainsPork { get; set; }
        public bool IsVegetarian { get; set; }
        public bool IsVegan { get; set; }
        public QrOrder.Domain.Enums.ServingTemperature ServingTemperature { get; set; }
        public string? ImageUrl { get; set; }
        public decimal Price { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsAvailable { get; set; } = true;
    }
}
