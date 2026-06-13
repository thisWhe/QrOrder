namespace QrOrder.Domain.Enums
{
    [Flags]
    public enum AllergenFlags
    {
        None = 0,
        Gluten = 1 << 0,
        Crustaceans = 1 << 1,
        Eggs = 1 << 2,
        Fish = 1 << 3,
        Peanuts = 1 << 4,
        Soy = 1 << 5,
        Milk = 1 << 6,
        Nuts = 1 << 7,
        Celery = 1 << 8,
        Mustard = 1 << 9,
        Sesame = 1 << 10,
        Sulphites = 1 << 11,
        Lupin = 1 << 12,
        Molluscs = 1 << 13
    }
}
