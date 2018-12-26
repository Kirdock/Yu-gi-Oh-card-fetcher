using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.Data;
using System.Linq;

namespace ConsoleApp2
{
    class Program
    {
        private static readonly string source = "https://www.etcg.de/yugioh/karten-suchmaschine/index.php";
        private static readonly string cdbFile = @"C:\Users\Klaus\Downloads\ygopro_vs_links_beta_windows\ygopro_vs_links_beta\cards.cdb";
        private static readonly HttpClient client = new HttpClient();
        private static SQLiteConnection sqlite;

        static void Main(string[] args)
        {
            sqlite = new SQLiteConnection($@"Data Source={cdbFile}");
            foreach (int id in readCDBFile())
            {
                string url = search(id);
                loadContent(url, id);
            }
        }

        private static string search(int id, bool padding = true)
        {
            var response = searchAsync(id, padding).Result;
            var responseHtml = getHtmlOfResponse(response).Result;

            var responseUrl = response.RequestMessage.RequestUri.ToString();

            if (responseUrl == source)
            {
                //not found or two cards

                //two cards?
                if (checkTwoCards(responseHtml, out string link))
                {
                    responseUrl = link;
                }
                //not fond, sometimes no padding works
                else if(padding)
                {
                    responseUrl = search(id, !padding);
                }
                //even without padding it is not working
                else
                {
                    Console.WriteLine($"Card not found. ID:{id}");
                    responseUrl = null;
                }
            }
            return responseUrl;
        }

        private static async Task<string> getHtmlOfResponse(HttpResponseMessage response)
        {
            return await response.Content.ReadAsStringAsync();
        }

        private static bool checkTwoCards(string html, out string link)
        {
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);
            link = doc.DocumentNode.SelectSingleNode("//table[@class='content_index']//tr[2]/td[2]/p/a")?.Attributes["href"].Value;
            bool result = link != null;

            if (result)
            {
                link = "https://www.etcg.de" + link;
            }

            return result;
        }

        private static int[] readCDBFile()
        {
            List<string> IDs = new List<string>();
            DataTable dt = new DataTable();
            try
            {
                sqlite.Open();
                SQLiteCommand cmd = sqlite.CreateCommand();
                cmd.CommandText = "Select id from texts";
                SQLiteDataAdapter ad = new SQLiteDataAdapter(cmd);
                ad.Fill(dt);
            }
            catch (SQLiteException ex)
            {
                Console.WriteLine($"Error while fetching data: {ex.Message}");
            }
            sqlite.Close();

            return dt.AsEnumerable().Select(row => int.Parse(row[0].ToString())).ToArray();
        }

        private static async Task<HttpResponseMessage> searchAsync(int idNumber, bool withPadding = true)
        {
            string id = idNumber.ToString();
            id = id.Length > 8 ? id.Substring(0, 8) : id;
            var values = new Dictionary<string, string>
            {
               { "gba_number", withPadding && id.Length < 8 ?  id.PadLeft(8, '0') : id},
               { "perform_search", "1" }
            };

            var content = new FormUrlEncodedContent(values);

            var response = await client.PostAsync(source, content);
            return response;
        }

        private static void loadContent(string url, int id, bool secondRun = false)
        {
            HtmlWeb web = new HtmlWeb();
            HtmlDocument doc = web.Load(url);

            HtmlNode cardName = doc.DocumentNode.SelectSingleNode("//div[@class='standard_content']/table//table"); //Kartenname
            HtmlNode cardDesc = doc.DocumentNode.SelectSingleNode("//div[@class='standard_content']/table//table[2]"); //Beschreibung
            if(cardDesc != null && cardName != null)
            {
                updateCDBFile(id, getHtmlText(cardName), getHtmlText(cardDesc));
            }
            else
            {
                Console.WriteLine($"Card with ID: {id} not found");
            }
        }

        private static string getHtmlText(HtmlNode node, bool specialCharacters = false)
        {
            string text = WebUtility.HtmlDecode(node.InnerText).Trim();
            return specialCharacters ? text : removeUmlaute(text);
        }

        private static string removeUmlaute(string text)
        {
            return text.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        }

        private static void updateCDBFile(int id, string name, string desc)
        {
            try
            {
                sqlite.Open();
                SQLiteCommand cmd = sqlite.CreateCommand();
                cmd.CommandText = $"update texts set name='{name}', desc='{desc}' where id={id}";
                cmd.ExecuteNonQuery();
                Console.WriteLine($"card with ID={id} updated");
            }
            catch (SQLiteException ex)
            {
                Console.WriteLine($"Error while updating data: {ex.Message}");
            }
            sqlite.Close();
        }
    }
}
