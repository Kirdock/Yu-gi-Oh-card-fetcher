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
using System.IO;
using System.Threading;
using System.Collections.Concurrent;

namespace ConsoleApp2
{
    class Program
    {
        private static readonly string domain = "https://www.etcg.de";
        private static readonly string source = domain+"/yugioh/karten-suchmaschine/index.php";
        private static string cdbFile;
        private static readonly HttpClient client = new HttpClient();
        private static SQLiteConnection sqlite;
        private static List<int> notFoundCards = new List<int>();
        private static ConcurrentQueue<int> workQueue = new ConcurrentQueue<int>();
        private static readonly int threadCount = 80;

        static void Main(string[] args)
        {
            Console.Write("Please enter Folder to cards.cdb:  ");
            
            cdbFile = checkFilePath(Console.ReadLine());
            if(cdbFile != null)
            {
                sqlite = new SQLiteConnection($@"Data Source={cdbFile}");
                sqlite.Open();
                workQueue = new ConcurrentQueue<int>(readCDBFile());
                var threadArray = new List<Thread>();
                for(int i = 0; i < threadCount; i++)
                {
                    var thread = new Thread(() =>
                    {
                        while (!workQueue.IsEmpty)
                        {
                            workQueue.TryDequeue(out int id);
                            if (id != 0)
                            {
                                string url = search(id);
                                if (url != null)
                                {
                                    loadContent(url, id);
                                }
                            }
                        }
                    });
                    threadArray.Add(thread);
                    thread.Start();
                }
                foreach(Thread thread in threadArray)
                {
                    thread.Join();
                }
                sqlite.Close();
                Console.WriteLine("finished");
            }
            Console.ReadLine();
        }

        private static string checkFilePath(string path)
        {
            if (path != null)
            {
                path = path.Trim(new char[] { '"' }); //when the path is already in quotation marks
                if (!path.EndsWith(@"\cards.cdb"))
                {
                    path += @"\cards.cdb";
                }
                
                if (!File.Exists(path))
                {
                    path = null;
                }
            }
            return path;
        }

        private static string search(int id, bool padding = true)
        {
            var response = searchAsync(id, padding).Result;
            var responseHtml = getHtmlOfResponse(response).Result;

            var responseUrl = response.RequestMessage.RequestUri.ToString();

            if (responseUrl == source)
            {
                //two cards for one ID?
                if (checkTwoCards(responseHtml, out string link))
                {
                    Console.WriteLine($"Two cards with same ID found. I'll take the first one. ID:{id}");
                    responseUrl = domain+link;
                }
                //not found, sometimes no padding works
                else if(padding)
                {
                    responseUrl = search(id, !padding);
                }
                //even without padding it is not working
                else
                {
                    Console.WriteLine($"Card not found. ID:{id}");
                    notFoundCards.Add(id);
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
            return link != null;
        }

        private static int[] readCDBFile()
        {
            List<string> IDs = new List<string>();
            DataTable dt = new DataTable();
            try
            {
                SQLiteCommand cmd = sqlite.CreateCommand();
                cmd.CommandText = "Select id from texts";
                SQLiteDataAdapter ad = new SQLiteDataAdapter(cmd);
                ad.Fill(dt);
            }
            catch (SQLiteException ex)
            {
                Console.WriteLine($"Error while fetching data: {ex.Message}");
            }
            Console.WriteLine("CDB-File loaded");
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

            return await client.PostAsync(source, content);
        }

        private static void loadContent(string url, int id, bool secondRun = false)
        {
            HtmlWeb web = new HtmlWeb();
            HtmlDocument doc = web.Load(url);

            HtmlNode cardName = doc.DocumentNode.SelectSingleNode("//div[@class='standard_content']/table//table"); //Card name
            HtmlNode cardDesc = doc.DocumentNode.SelectSingleNode("//div[@class='standard_content']/table//table[2]"); //Card description
            if (cardDesc != null && cardName != null)
            {
                updateCDBFile(id, getHtmlText(cardName), getHtmlText(cardDesc));
            }
            else
            {
                Console.WriteLine($"Card not found. ID:{id}");
            }
        }

        private static string getHtmlText(HtmlNode node, bool specialCharacters = false)
        {
            string text = WebUtility.HtmlDecode(node.InnerText).Trim();
            return specialCharacters ? text : removeSpecialCharacters(text);
        }

        private static string removeSpecialCharacters(string text)
        {
            return text.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        }

        private static void updateCDBFile(int id, string name, string desc)
        {
            try
            {
                SQLiteCommand cmd = sqlite.CreateCommand();
                cmd.CommandText = $"update texts set name='{name}', desc='{desc}' where id={id}";
                cmd.ExecuteNonQuery();
                Console.WriteLine($"Card with ID={id} updated");
            }
            catch (SQLiteException ex)
            {
                Console.WriteLine($"Error while updating data: {ex.Message}, id:{id}, name:{name}, desc:{desc}");
            }
        }
    }
}
