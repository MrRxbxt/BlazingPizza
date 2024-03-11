
namespace BlazingPizza
{
    public class Topping
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public decimal Price { get; set; }

        public string GetFormattedPrice() => Price.ToString("0.00");

        public static implicit operator Topping(PizzaTopping pizzaTopping)
        {
            if (pizzaTopping == null)
            {
                return null; // Handle the case where pizzaTopping is null
            }

            return new Topping
            {
                Id = pizzaTopping.ToppingId,
                // Map other properties as needed
            };
        }
    }
}
