
using Itinero;
using Itinero.IO.Osm;
using Itinero.LocalGeo;
using Itinero.Osm.Vehicles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace RotaApiArge.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class RotaController : ControllerBase
    {
        private readonly IMemoryCache _cache;

        public RotaController(IMemoryCache cache)
        {
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> Rota([FromQuery] double startLat, [FromQuery] double startLng, [FromQuery] double endLat, [FromQuery] double endLng)
        {
            var start = new Coordinate(((float)startLat), (float)startLng);
            var end = new Coordinate((float)endLat, (float)endLng);

            var cacheKey = $"{start.Latitude},{start.Longitude}_{end.Latitude},{end.Longitude}";

            var cachedRoute = _cache.Get<string>(cacheKey);
            if (!string.IsNullOrEmpty(cachedRoute))
            {
                return Content(cachedRoute, "application/json");
            }

            var routerDb = new RouterDb();

            await Task.Run(() =>
            {
                using (var stream = new System.IO.FileStream(@"D:\Kitap\Rotalama_Alogirtma\planet_28.943,40.675_30.242,41.277.osm.pbf", System.IO.FileMode.Open))
                {
                    routerDb.LoadOsmData(stream, Vehicle.Car);
                }
            });

            var router = new Router(routerDb);

            var startCoordinate = new Coordinate((float)start.Latitude, (float)start.Longitude);
            var endCoordinate = new Coordinate((float)end.Latitude, (float)end.Longitude);

            var route = router.Calculate(Vehicle.Car.Fastest(), startCoordinate, endCoordinate);
            var testRoute = route.ToGeoJson();
            testRoute = testRoute.Trim('"').Replace("\\", "");

            _cache.Set(cacheKey, testRoute, TimeSpan.FromMinutes(10));

            return Content(testRoute, "application/json");
        }
    }

}
