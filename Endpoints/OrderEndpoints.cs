using BlazingPizza.APIServices;
using BlazingPizza.Shared;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Net;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace BlazingPizza.Endpoints
{
    public static class OrderEndpoints
    {

        public static void MapOrdersEndpoints(this IEndpointRouteBuilder builder)
        {
            var group = builder.MapGroup("orders");

            group.MapGet("", async (SqlConnectionFactory sqlConnectionFactory) =>
            {
                using var connection = sqlConnectionFactory.Create();
                await connection.OpenAsync();

                const string getOrdersSql = "SELECT * FROM Orders";
                
                var dbOrders = await connection.QueryAsync(getOrdersSql);
                
                if (dbOrders == null) return Results.Empty;

                List<OrderWithStatus> ordersWithStatus = new List<OrderWithStatus>();
                foreach (var dbOrder in dbOrders)
                {
                    var order = await GetOrder(connection, dbOrder);
                    ordersWithStatus.Add(OrderWithStatus.FromOrder(order));
                }

                return Results.Ok(ordersWithStatus.ToList());
            });

            group.MapGet("{orderId}", async (int orderId, SqlConnectionFactory sqlConnectionFactory) =>
            {
                if (orderId < 1) return Results.BadRequest();

                using var connection = sqlConnectionFactory.Create();
                await connection.OpenAsync();

                const string getOrderSql = "SELECT * FROM Orders WHERE OrderId = @OrderId";
                var dbOrder = await connection.QueryFirstOrDefaultAsync(getOrderSql, new { OrderId = orderId });
                
                if (dbOrder == null) return Results.NotFound();

                var order = await GetOrder(connection, dbOrder);

                return order is not null ? Results.Ok(OrderWithStatus.FromOrder(order)) : Results.NotFound();
            });

            group.MapPost("", async (Order order, SqlConnectionFactory sqlConnectionFactory) =>
            {
                if (order == null) return Results.BadRequest();

                using var connection = sqlConnectionFactory.Create();
                await connection.OpenAsync();

                using var transaction = connection.BeginTransaction();

                order.CreatedTime = DateTime.Now;

                try
                {
                    int? deliveryAddressId = null;

                    // Insert delivery address
                    if (order.DeliveryAddress != null)
                    {
                        deliveryAddressId = await connection.ExecuteScalarAsync<int>(@"
                            INSERT INTO Address (Name, Line1, Line2, City, Region, PostalCode)
                            VALUES (@Name, @Line1, @Line2, @City, @Region, @PostalCode);
                            SELECT CAST(SCOPE_IDENTITY() as int)", order.DeliveryAddress, transaction);
                    }

                    // Insert order and retrieve the last inserted OrderId
                    order.OrderId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO Orders (UserId, CreatedTime, DeliveryAddressId)
                        VALUES (@UserId, @CreatedTime, @DeliveryAddressId);
                        SELECT CAST(SCOPE_IDENTITY() as int)", new
                            {
                                order.UserId,
                                order.CreatedTime,
                                DeliveryAddressId = deliveryAddressId
                            }, transaction);

                    foreach (var pizza in order.Pizzas)
                    {
                        pizza.OrderId = order.OrderId;

                        // Insert pizza
                        int pizzaId = await connection.ExecuteScalarAsync<int>(@"
                            INSERT INTO Pizzas (OrderId, SpecialId, Size)
                            VALUES (@OrderId, @SpecialId, @Size);
                            SELECT CAST(SCOPE_IDENTITY() as int)", pizza, transaction);

                        // Insert pizza toppings
                        foreach (var topping in pizza.Toppings)
                        {
                            topping.PizzaId = pizzaId;

                            // Insert topping only if it exists in the Toppings table
                            await connection.ExecuteAsync(@"
                                INSERT INTO PizzaToppings (PizzaId, ToppingId)
                                VALUES (@PizzaId, @ToppingId);
                            ", topping, transaction);
                        }
                    }

                    // Commit the transaction if everything is successful
                    transaction.Commit();

                    return Results.Ok(order.OrderId);
                }
                catch (Exception)
                {
                    // Handle exceptions, log, or rollback the transaction
                    transaction.Rollback();
                    return Results.BadRequest("Failed to create the order");
                }
            });
        }

        private static async Task<Order> GetOrder(SqlConnection connection, dynamic dbOrder)
        {
            var orderId = dbOrder.OrderId;
            var deliveryAddressId = dbOrder.DeliveryAddressId;

            Order order = new()
            {
                OrderId = orderId,
                UserId = dbOrder.UserId,
                CreatedTime = dbOrder.CreatedTime
            };

            //address
            const string getAddressSql = "SELECT * FROM Address WHERE Id = @Id";
            Address address = await connection.QueryFirstAsync<Address>(getAddressSql, new { Id = deliveryAddressId });
            order.DeliveryAddress = address;

            //pizzas
            const string getPizzasSql = "SELECT * FROM Pizzas WHERE OrderId = @OrderId";
            var dbPizzas = await connection.QueryAsync(getPizzasSql, new { OrderId = orderId });
            List<Pizza> pizzas = new List<Pizza>();
            foreach (var dbPizza in dbPizzas)
            {
                var pizzaId = dbPizza.Id;
                var specialId = dbPizza.SpecialId;

                Pizza pizza = new()
                {
                    Id = dbPizza.Id,
                    OrderId = dbPizza.OrderId,
                    SpecialId = specialId,
                    Size = dbPizza.Size
                };

                //special
                const string getSpecialSql = "SELECT * FROM Specials WHERE Id = @Id";
                PizzaSpecial pizzaSpecial = await connection.QueryFirstAsync<PizzaSpecial>(getSpecialSql, new { Id = specialId });
                pizza.Special = pizzaSpecial;

                //pizzatopping
                const string getPizzaToppingsSql = "SELECT * FROM PizzaTopping WHERE PizzaId = @PizzaId";
                var dbPizzaToppings = await connection.QueryAsync(getPizzaToppingsSql, new { PizzaId = pizzaId });
                List<PizzaTopping> pizzaToppings = new List<PizzaTopping>();
                foreach (var dbPizzaTopping in dbPizzaToppings)
                {
                    var toppingId = dbPizzaTopping.ToppingId;

                    PizzaTopping pizzaTopping = new()
                    {
                        PizzaId = pizzaId,
                        ToppingId = toppingId
                    };

                    //topping
                    const string getToppingSql = "SELECT * FROM Toppings WHERE Id = @Id";
                    var dbTopping = await connection.QueryFirstAsync<Topping>(getToppingSql, new { Id = toppingId });
                    pizzaTopping.Topping = dbTopping;
                }
                pizza.Toppings = pizzaToppings;

                pizzas.Add(pizza);
            }

            order.Pizzas = pizzas;

            return order;
        }
    }
}
