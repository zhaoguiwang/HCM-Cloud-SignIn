using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Xamarin.Forms;

namespace HcmCloudRemoteSignInMobile
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            User user = GetUser();
            account.Text = user.username;
            password.Text = user.password;
            userAgent.Text = user.useragent;
            isRandom.IsToggled = user.isRandom;
        }
        private void Signin_Clicked(object sender, EventArgs e)
        {
            try
            {
                IList<RestResponseCookie> cookies=GetCookie();
                RemoteSignInAsync(cookies);
            }
            catch (Exception exception)
            {
                log.Text += "\n" + exception.Message;
            }
        }
        private IList<RestResponseCookie> GetCookie()
        {
            IList<RestResponseCookie> cookies = new List<RestResponseCookie>();
            try
            {
                var client = new RestClient("https://inspur.hcmcloud.cn/login?sso=emmcloud");
                client.FollowRedirects = false;
                var request = new RestRequest(Method.GET);
                IRestResponse response = client.Execute(request);//1
                string location = response.Headers.ToList().Find(x => x.Name == "Location").Value.ToString();
                client = new RestClient(location);
                client.FollowRedirects = false;
                request = new RestRequest(Method.GET);
                response = client.Execute(request);//2
                location = response.Headers.ToList().Find(x => x.Name == "Location").Value.ToString();
                cookies = response.Cookies;

                client = new RestClient(location);
                request = new RestRequest(Method.GET);
                foreach (var item in response.Cookies)
                {
                    request.AddCookie(item.Name, item.Value);
                }
                response = client.Execute(request);//3
                string result = response.Content;
                MatchCollection mc = Regex.Matches(result, "(?<=name=\"execution\" value=\")[\\w-=]*");
                var execution = mc[0].Value;



                client = new RestClient(location);
                client.FollowRedirects = false;
                request = new RestRequest(Method.POST);
                request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
                string body = $"username={HttpUtility.UrlEncode(account.Text, Encoding.UTF8)}&password={HttpUtility.UrlEncode(password.Text, Encoding.UTF8)}&execution={HttpUtility.UrlEncode(execution, Encoding.UTF8)}&_eventId=submit";
                request.AddParameter("undefined", body, ParameterType.RequestBody);
                response = client.Execute(request);//4
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new Exception("用户名/密码错误，请检查");
                }
                cookies = cookies.Union(response.Cookies).ToList();
                location = response.Headers.ToList().Find(x => x.Name == "Location").Value.ToString();

                client = new RestClient(location);
                client.FollowRedirects = false;
                request = new RestRequest(Method.GET);
                foreach (var item in cookies)
                {
                    request.AddCookie(item.Name, item.Value);
                }
                response = client.Execute(request);//5
                location = response.Headers.ToList().Find(x => x.Name == "Location").Value.ToString();
                cookies[0] = response.Cookies[0];

                client = new RestClient(location);
                client.FollowRedirects = false;
                request = new RestRequest(Method.GET);
                foreach (var item in cookies)
                {
                    request.AddCookie(item.Name, item.Value);
                }
                response = client.Execute(request);//6
                location = response.Headers.ToList().Find(x => x.Name == "Location").Value.ToString();

                client = new RestClient(location);
                client.FollowRedirects = false;
                request = new RestRequest(Method.GET);
                response = client.Execute(request);//7
                cookies = response.Cookies;
                if (cookies.Count == 2)
                {
                    log.Text += "\n" + "获取Cookie成功";
                }
                else
                {
                    throw new Exception(response.ErrorMessage);
                }
            }
            catch (Exception e)
            {
                throw new Exception("获取Cookie失败：" + e.Message);
            }
            return cookies;
        }
        private string GetCCWorkVersion()
        {
            var client = new RestClient("https://b.ccwork.com/htime/htime/queryOneVersion");
            var request = new RestRequest(Method.POST);
            request.AddHeader("Content-Type", "application/json");
            request.AddParameter("undefined", "{\"appType\":\"1\",\"actionCode\":\"1\"}", ParameterType.RequestBody);
            IRestResponse response = client.Execute(request);
            var version = JsonConvert.DeserializeObject<VersionEntity>(response.Content).data.versioname;
            log.Text += "\nccwork版本为：" + version;
            return version;
        }
        private async Task<Location> GetRandomLocationAsync(IList<RestResponseCookie> cookies)
        {
            var client = new RestClient("https://inspur.hcmcloud.cn/api/attend.get.emp.location.list");
            var request = new RestRequest(Method.POST);
            foreach (var item in cookies)
            {
                request.AddCookie(item.Name, item.Value);
            }
            IRestResponse response = client.Execute(request);
            var locationList = JsonConvert.DeserializeObject<LocationListEntity>(response.Content);
            LocationEntity location;
            if (isRandom.IsToggled)
            {
                location = locationList.result[new Random().Next() % locationList.result.Count];//随机获取一个打卡点
            }
            else
            {
                string action = await DisplayActionSheet("请选择签到地点", null, null, locationList.result.Select(item => item.address).ToArray());
                location = locationList.result.Where(item => item.address == action).First();
            }
            Location randomLocation = new Location(location.longitude, location.latitude).GetRandomLocation(location.radius);//随机签到点
            randomLocation.id = location.id;
            randomLocation.distance = randomLocation.GetDistance(new Location(location.longitude, location.latitude));
            log.Text += $"\n共获取到{locationList.result.Count}处打卡点，本次随机打卡点为{location.address}附近{randomLocation.distance}米";
            log.Text += $"\n({randomLocation.longitude},{randomLocation.latitude})";
            return randomLocation;
        }
        private async Task RemoteSignInAsync(IList<RestResponseCookie> cookies)
        {
            var versioname = GetCCWorkVersion();
            var client = new RestClient("https://inspur.hcmcloud.cn/api/attend.signin.create");
            if (string.IsNullOrEmpty(userAgent.Text))
            {
                client.UserAgent = "Mozilla/5.0 (Linux; Android 10; SEA-AL10 Build/HUAWEISEA-AL10; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/78.0.3904.108 Mobile Safari/537.36/emmcloud/ccwork/" + versioname;
                userAgent.Text = client.UserAgent;
            }
            else
            {
                userAgent.Text = Regex.Replace(userAgent.Text, @"(?<=ccwork\/)[\d.]*", versioname);
                client.UserAgent = userAgent.Text;
            }
            var request = new RestRequest(Method.POST);
            request.AddHeader("Content-Type", "application/json;charset=UTF-8");
            request.AddHeader("Host", "inspur.hcmcloud.cn");
            request.AddHeader("Connection", "keep-alive");
            request.AddHeader("Accept", "application/json,text/plain, */*");
            request.AddHeader("Origin", "https://inspur.hcmcloud.cn");
            request.AddHeader("X-Requested-With", "com.inspur.playwork.internet");
            request.AddHeader("Sec-Fetch-Site", "same-origin");
            request.AddHeader("Sec-Fetch-Mode", "cors");
            request.AddHeader("Referer", "https://inspur.hcmcloud.cn/");
            request.AddHeader("Accept-Encoding", "gzip, deflate");
            request.AddHeader("Accept-Language", "zh-CN, zh; q=0.9, en-US; q=0.8, en; q=0.7");
            Dictionary<string, object> body = new Dictionary<string, object>();
            //获取随机签到地点
            Location location = await GetRandomLocationAsync(cookies);
            body.Add("location_id", location.id);
            body.Add("type", 3);
            body.Add("latitude", location.latitude);
            body.Add("longitude", location.longitude);
            body.Add("beacon", "");
            DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1, 0, 0, 0, 0));
            long time = (DateTime.Now.Ticks - startTime.Ticks) / 10000;
            body.Add("timestamp", time);
            body.Add("information", $"{{\"source\":\"gps\",\"distance\":{location.distance},\"accuracy\":0}}");
            body.Add("images", "");
            body.Add("hash", GenerateMD5($"{location.id}3{location.latitude}{location.longitude}{time}hcm cloud"));
            request.AddParameter("undefined", JsonConvert.SerializeObject(body), ParameterType.RequestBody);
            foreach (var item in cookies)
            {
                request.AddCookie(item.Name, item.Value);
            }
            var response = client.Execute(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                log.Text += "\n签到成功";
                User user = new User();
                user.username = account.Text;
                user.password = password.Text;
                user.useragent = userAgent.Text;
                user.isRandom = isRandom.IsToggled;
                SaveUser(user);
            }
            else
            {
                log.Text += "\n" + "签到失败";
                throw new Exception(response.ErrorMessage);
            }
        }
        private string GenerateMD5(string txt)
        {
            using (MD5 mi = MD5.Create())
            {
                byte[] buffer = Encoding.Default.GetBytes(txt);
                //开始加密
                byte[] newBuffer = mi.ComputeHash(buffer);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < newBuffer.Length; i++)
                {
                    sb.Append(newBuffer[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
        public void SaveUser(User user)
        {

            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "config.json");
            using (FileStream fs = File.Create(filePath))
            {
                var str = JsonConvert.SerializeObject(user);
                byte[] info = new UTF8Encoding(true).GetBytes(str);
                fs.Write(info, 0, info.Length);
            }
        }
        public User GetUser()
        {
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "config.json");
            if (File.Exists(filePath))
            {
                return JsonConvert.DeserializeObject<User>(File.ReadAllText(filePath));
            }
            else
            {
                return new User();
            }
        }
    }
    public class VersionEntity
    {
        public DataEntity data;
    }
    public class DataEntity
    {
        public string versioname;
    }
    public class LocationListEntity
    {
        public List<LocationEntity> result;
    }
    public class LocationEntity
    {
        public int id;
        public double longitude;
        public double latitude;
        public double radius;
        public string address;
    }
    public class User
    {
        public string username;
        public string password;
        public string useragent;
        public bool isRandom = true;
    }
    public class Location
    {
        //经度
        public double longitude;
        //纬度
        public double latitude;
        public int id;
        public int distance;
        //地球半径，单位米
        private const double EARTH_RADIUS = 6378137;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="longitude">精度</param>
        /// <param name="latitude">纬度</param>
        public Location(double longitude, double latitude)
        {
            this.longitude = longitude;
            this.latitude = latitude;
        }
        public Location() { }
        /// <summary>
        /// 给出距当前位置指定距离内的随机点
        /// </summary>
        /// <param name="distance"></param>
        /// <returns></returns>
        public Location GetRandomLocation(double distance)
        {
            if (distance <= 0) distance = 70;
            double lat, lon, brg;
            Location location = new Location();
            double maxdist = distance;
            maxdist = maxdist / EARTH_RADIUS;
            double startlat = Rad(latitude);
            double startlon = Rad(longitude);
            var cosdif = Math.Cos(maxdist) - 1;
            var sinstartlat = Math.Sin(startlat);
            var cosstartlat = Math.Cos(startlat);
            double dist = 0;
            var rad360 = 2 * Math.PI;
            dist = Math.Acos((new Random().NextDouble() * cosdif + 1));
            brg = rad360 * new Random().NextDouble();
            lat = Math.Asin(sinstartlat * Math.Cos(dist) + cosstartlat * Math.Sin(dist) * Math.Cos(brg));
            lon = Deg(NormalizeLongitude(startlon * 1 + Math.Atan2(Math.Sin(brg) * Math.Sin(dist) * cosstartlat, Math.Cos(dist) - sinstartlat * Math.Sin(lat))));
            lat = Deg(lat);
            location.latitude = PadZeroRight(lat);
            location.longitude = PadZeroRight(lon);
            return location;
        }

        /// <summary>
        /// 计算当前位置与给定位置间的距离
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public int GetDistance(Location position)
        {
            double radLat1 = Rad(latitude);
            double radLng1 = Rad(longitude);
            double radLat2 = Rad(position.latitude);
            double radLng2 = Rad(position.longitude);
            double a = radLat1 - radLat2;
            double b = radLng1 - radLng2;
            int result = (int)(2 * Math.Asin(Math.Sqrt(Math.Pow(Math.Sin(a / 2), 2) + Math.Cos(radLat1) * Math.Cos(radLat2) * Math.Pow(Math.Sin(b / 2), 2))) * EARTH_RADIUS);
            return result;
        }

        /// <summary>
        /// 经纬度转化成弧度
        /// </summary>
        /// <param name="d"></param>
        /// <returns></returns>
        private static double Rad(double d)
        {
            return (double)d * Math.PI / 180d;
        }
        /// <summary>
        /// 角度
        /// </summary>
        /// <param name="rd"></param>
        /// <returns></returns>
        private static double Deg(double rad)
        {
            return (rad * 180d / Math.PI);
        }
        private static double NormalizeLongitude(double longitude)
        {
            var n = Math.PI;
            if (longitude > n)
            {
                longitude = longitude - 2 * n;
            }
            else if (longitude < -n)
            {
                longitude = longitude + 2 * n;
            }
            return longitude;
        }
        private static double PadZeroRight(double s)
        {
            double sigDigits = 12;
            s = Math.Round(s * Math.Pow(10, sigDigits)) / Math.Pow(10, sigDigits);
            return s;
        }
    }
}
