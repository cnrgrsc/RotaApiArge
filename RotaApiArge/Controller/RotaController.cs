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
			_cache = cache;  // Önbellek referansını sınıf değişkenine atar
			_routerDb = _cache.GetOrCreate("RouterDb", entry =>  // "RouterDb" anahtarını kullanarak önbellekte bir RouterDb nesnesi arar veya oluşturur
			{
				entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);  // Önbellekteki öğenin geçerliliği 24 saat olarak ayarlanır
				RouterDb routerDb = new RouterDb();  // Yeni bir RouterDb nesnesi oluşturur
				using (var stream = new FileStream(OSM_PATH, FileMode.Open, FileAccess.Read))  // OSM veri dosyasını okumak için bir dosya akışı oluşturur
				{
					routerDb.LoadOsmData(stream, Vehicle.Car);  // OSM verilerini RouterDb nesnesine yükler
				}
				return routerDb;  // RouterDb nesnesini döndürür, böylece önbellekte saklanabilir
			});
		}



		[HttpGet]
		public IActionResult GetRoute()
		{
			// Yeni bir Stopwatch nesnesi oluşturur, bu kodun çalışma süresini ölçmek için kullanılacaktır.
			Stopwatch Tim = new Stopwatch();

			// Başlangıç ve bitiş koordinatlarını tanımlar
			Coordinate START_COORDINATE = new Coordinate(41.0484f, 29.0537f);
			Coordinate END_COORDINATE = new Coordinate(41.0799f, 29.0690f);

			try
			{
				// Önbellekte 'RouteOrder' anahtarı altında bir değer olup olmadığını kontrol eder.
				// Eğer varsa, bu değeri 'routeOrderCoordinates' değişkenine atar.
				if (!_cache.TryGetValue("RouteOrder", out List<CoordinateModel> routeOrderCoordinates))
				{
					// JSON dosyasını okur ve içeriğini bir string'e dönüştürür.
					string json = System.IO.File.ReadAllText(JSON_PATH);
					// JSON string'ini bir FeatureCollection nesnesine dönüştürür.
					var featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(json);

					// Koordinat listesi oluşturur ve başlangıç koordinatını listeye ekler
					List<Coordinate> coordinates = new List<Coordinate> { START_COORDINATE };
					// FeatureCollection'daki her özelliği (feature) döngüsü ile işler ve koordinatları listeye ekler
					foreach (var feature in featureCollection.features)
					{
						float lat = feature.geometry.coordinates[1];
						float lon = feature.geometry.coordinates[0];
						coordinates.Add(new Coordinate(lat, lon));
					}
					// Bitiş koordinatını listeye ekler
					coordinates.Add(END_COORDINATE);

					// Mesafe matrisini başlatır, bu matris koordinatlar arasındaki mesafeleri saklayacaktır.
					float[,] distances = new float[coordinates.Count, coordinates.Count];
					// Önceden yüklenmiş RouterDb'yi kullanarak yeni bir router nesnesi oluşturur.
					var router = new ItineroRouter(_routerDb);
					// Zamanlayıcıyı başlatır
					Tim.Start();

					// Koordinatlar listesindeki her koordinat için döngü başlatır
					for (int i = 0; i < coordinates.Count; i++)
					{
						// İlk koordinatı çözümleyerek bir router nesnesi oluşturur
						var resolved1 = router.Resolve(Vehicle.Car.Shortest(), coordinates[i], SEARCH_DISTANCE);
						// Eğer koordinat çözümlenemezse, hatayı konsola yazar ve bir sonraki iterasyona geçer
						if (resolved1 == null)
						{
							Console.WriteLine($"Failed to resolve point at coordinates {coordinates[i].Latitude}, {coordinates[i].Longitude}");
							continue;
						}

						// İkinci koordinatı çözümleyerek bir router nesnesi oluşturur
						for (int j = i + 1; j < coordinates.Count; j++)
						{
							var resolved2 = router.Resolve(Vehicle.Car.Shortest(), coordinates[j], SEARCH_DISTANCE);
							// Eğer koordinat çözümlenemezse, hatayı konsola yazar ve bir sonraki iterasyona geçer
							if (resolved2 == null)
							{
								Console.WriteLine($"Failed to resolve point at coordinates {coordinates[j].Latitude}, {coordinates[j].Longitude}");
								continue;
							}

							// İki koordinat arasındaki rotayı hesaplar
							var route = router.Calculate(Vehicle.Car.Shortest(), resolved1, resolved2);

							// Hesaplanan mesafeyi mesafe matrisine ekler
							distances[i, j] = route.TotalDistance;
							distances[j, i] = route.TotalDistance;
						}
					}
					// Rota sırasını belirlemek için asenkron bir işlem başlatır
					routeOrderCoordinates = DetermineRouteOrderAsync(coordinates, distances);
					// Zamanlayıcıyı durdurur ve geçen toplam süreyi konsola yazar
					Tim.Stop();
					Console.WriteLine(Tim.Elapsed.TotalSeconds);
				}

				// Rota sırası koordinatlarını döndürür
				return Ok(routeOrderCoordinates);
			}
			catch (Exception ex)
			{
				// Herhangi bir hata durumunda hatayı konsola yazar ve bir BadRequest yanıtı döndürür
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return BadRequest(ex.Message);
			}
		}


		private List<CoordinateModel> DetermineRouteOrderAsync(List<Coordinate> coordinates, float[,] distances)
		{
			// Rota sırasındaki koordinatları saklamak için yeni bir CoordinateModel listesi oluşturur
			List<CoordinateModel> routeOrderCoordinates = new List<CoordinateModel>();
			// Ziyaret edilmemiş düğümleri temsil etmek için bir hash set oluşturur. Başlangıçta tüm düğümler ziyaret edilmemiştir.
			HashSet<int> unvisited = new HashSet<int>(Enumerable.Range(0, coordinates.Count));

			int current = 0; // Başlangıç noktasını temsil eder (0. indeks)
							 // Başlangıç koordinatını rota sırası listesine ekler
			routeOrderCoordinates.Add(new CoordinateModel { x = coordinates[current].Longitude, y = coordinates[current].Latitude });
			// Başlangıç düğümünü ziyaret edilmiş olarak işaretler ve unvisited setinden kaldırır
			unvisited.Remove(current);

			// Tüm düğümler ziyaret edilene kadar döngüyü sürdürür
			while (unvisited.Count > 0)
			{
				// En yakın düğümü bulmak için başlangıç değerlerini belirler
				float closestDistance = float.MaxValue;
				int closestNode = -1;

				// Ziyaret edilmemiş her düğüm için döngüyü çalıştırır
				foreach (int i in unvisited)
				{
					// Eğer bulunan mesafe, şu ana kadar bulunan en kısa mesafeden daha küçükse, en yakın düğümü ve mesafeyi günceller
					if (distances[current, i] < closestDistance)
					{
						closestDistance = distances[current, i];
						closestNode = i;
					}
				}

				// En yakın düğümü mevcut düğüm olarak günceller ve ziyaret edilmiş olarak işaretler
				current = closestNode;
				unvisited.Remove(current);
				// Mevcut düğümün koordinatlarını rota sırası listesine ekler
				routeOrderCoordinates.Add(new CoordinateModel
				{
					x = coordinates[current].Longitude,
					y = coordinates[current].Latitude
				});
			}

			// Rota sırasındaki koordinatları döndürür
			return routeOrderCoordinates;
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


