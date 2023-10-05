using System.Diagnostics;
using Itinero;
using Itinero.IO.Osm;
using Itinero.LocalGeo;
using Itinero.Osm.Vehicles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using ItineroRouter = Itinero.Router;

namespace RotaApiArge.Controller
{
	[Route("api/[controller]")]
	[ApiController]
	public class RotaController : ControllerBase
	{
		private const string JSON_PATH = @"D:\Kitap\Rotalama_Alogirtma\eczane.json";
		private const string OSM_PATH = @"D:\Kitap\Rotalama_Alogirtma\Istanbul.osm.pbf";
		private const float SEARCH_DISTANCE = 50f;
		private readonly IMemoryCache _cache;
		private readonly RouterDb _routerDb;

		public RotaController(IMemoryCache cache)
		{
			_cache = cache;
			_routerDb = new RouterDb();

			using (var stream = new FileStream(OSM_PATH, FileMode.Open, FileAccess.Read))
			{
				_routerDb.LoadOsmData(stream, Vehicle.Car);
			}
		}

		[HttpGet]
		public async Task<IActionResult> GetRoute()
		{
			Stopwatch Tim = new Stopwatch();
			
			Coordinate START_COORDINATE = new Coordinate(41.0484f, 29.0537f);
			Coordinate END_COORDINATE = new Coordinate(41.0799f, 29.0690f);

			try
			{
				if (!_cache.TryGetValue("RouteOrder", out List<CoordinateModel> routeOrderCoordinates))
				{
					// JSON dosyasından veriyi oku ve dönüştür
					string json = await System.IO.File.ReadAllTextAsync(JSON_PATH);
					var featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(json);

					// Koordinat listesi oluştur
					List<Coordinate> coordinates = new List<Coordinate> { START_COORDINATE };
					foreach (var feature in featureCollection.features)
					{
						float lat = feature.geometry.coordinates[1];
						float lon = feature.geometry.coordinates[0];
						coordinates.Add(new Coordinate(lat, lon));
					}
					coordinates.Add(END_COORDINATE);

					// Mesafeleri hesapla
					float[,] distances = new float[coordinates.Count, coordinates.Count];
					var router = new ItineroRouter(_routerDb); // Use the pre-loaded RouterDb.
					Tim.Start();
					await Task.Run(() =>
					{
						for (int i = 0; i < coordinates.Count; i++)
						{
							var resolved1 = router.Resolve(Vehicle.Car.Shortest(), coordinates[i], SEARCH_DISTANCE);
							if (resolved1 == null)
							{
								Console.WriteLine($"Failed to resolve point at coordinates {coordinates[i].Latitude}, {coordinates[i].Longitude}");
								continue;
							}

							for (int j = i + 1; j < coordinates.Count; j++)
							{
								var resolved2 = router.Resolve(Vehicle.Car.Shortest(), coordinates[j], SEARCH_DISTANCE);
								if (resolved2 == null)
								{
									Console.WriteLine($"Failed to resolve point at coordinates {coordinates[j].Latitude}, {coordinates[j].Longitude}");
									continue;
								}

								var route = router.Calculate(Vehicle.Car.Shortest(), resolved1, resolved2);


								distances[i, j] = route.TotalDistance;
								distances[j, i] = route.TotalDistance;
							}


						}
					});
					
					
					// Rota sırasını belirle
					
					
					routeOrderCoordinates =await DetermineRouteOrderAsync(coordinates, distances);
					Tim.Stop();
					Console.WriteLine(Tim.Elapsed.TotalSeconds);
					//_cache.Set("RouteOrder", routeOrderCoordinates, TimeSpan.FromHours(1)); // 1 saat boyunca cache'de sakla
				}

				return Ok(routeOrderCoordinates);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return BadRequest(ex.Message);
			}
		}

		private async Task<List<CoordinateModel>> DetermineRouteOrderAsync(List<Coordinate> coordinates, float[,] distances)
		{
			return await Task.Run(() =>
			{
				List<CoordinateModel> routeOrderCoordinates = new List<CoordinateModel>();
				HashSet<int> unvisited = new HashSet<int>(Enumerable.Range(0, coordinates.Count));

				int current = 0; // Başlangıç noktası (0. indeks)
				routeOrderCoordinates.Add(new CoordinateModel { x = coordinates[current].Longitude, y = coordinates[current].Latitude });
				unvisited.Remove(current);

				while (unvisited.Count > 0)
				{
					float closestDistance = float.MaxValue;
					int closestNode = -1;

					foreach (int i in unvisited)
					{
						if (distances[current, i] < closestDistance)
						{
							closestDistance = distances[current, i];
							closestNode = i;
						}
					}

					current = closestNode;
					unvisited.Remove(current);
					routeOrderCoordinates.Add(new CoordinateModel { x = coordinates[current].Longitude, y = coordinates[current].Latitude });
				}

				return routeOrderCoordinates;
			});
		}



	}

	public class CoordinateModel
	{
		public float x { get; set; }
		public float y { get; set; }
	}

	public class FeatureCollection
	{
		public string type { get; set; }
		public Feature[] features { get; set; }
	}

	public class Feature
	{
		public string type { get; set; }
		public Geometry geometry { get; set; }
		public Properties properties { get; set; }
	}

	public class Geometry
	{
		public string type { get; set; }
		public float[] coordinates { get; set; }
	}

	public class Properties
	{

		public float pharmacyId { get; set; }
		public string pharmacyName { get; set; }
		public string address { get; set; }
		public string phoneNumber { get; set; }
		public int districtId { get; set; }
		public string districtName { get; set; }
		public int neighborhoodId { get; set; }
		public string neighborhoodName { get; set; }
		public int nightDuty { get; set; }
		public int roadId { get; set; }
		public float openingHour { get; set; }
		public float closingHour { get; set; }

	}

}


