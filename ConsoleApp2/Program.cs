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
using System.IO;
using System.Threading;
using System.Collections.Concurrent;

namespace ConsoleApp2
{
    class Program
    {
        private static string cdbFile;
        private static SQLiteConnection sqlite;
        private static ConcurrentQueue<string> workQueue = new ConcurrentQueue<string>();
        private static readonly int threadCount = 5;
        private static readonly string wiki = "http://yugioh.wikia.com/wiki/";

        static void Main(string[] args)
        {
            Console.Write("Please enter Folder to cards.cdb:  ");
            
            cdbFile = checkFilePath(Console.ReadLine());
            if(cdbFile != null)
            {
                sqlite = new SQLiteConnection($@"Data Source={cdbFile}");
                sqlite.Open();
                workQueue = new ConcurrentQueue<string>(readCDBFile());
                var threadArray = new List<Thread>();
                for (int i = 0; i < threadCount; i++)
                {
                    var thread = new Thread(() =>
                    {
                        while (!workQueue.IsEmpty)
                        {
                            workQueue.TryDequeue(out string name);
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                loadContentWiki(name);
                            }
                        }
                    });
                    threadArray.Add(thread);
                    thread.Start();
                }
                foreach (Thread thread in threadArray)
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

        private static string[] readCDBFile()
        {
            DataTable dt = new DataTable();
            try
            {
                SQLiteCommand cmd = sqlite.CreateCommand();
                cmd.CommandText = "Select name from texts";
                SQLiteDataAdapter ad = new SQLiteDataAdapter(cmd);
                ad.Fill(dt);
            }
            catch (SQLiteException ex)
            {
                Console.WriteLine($"Error while fetching data: {ex.Message}");
            }
            Console.WriteLine("CDB-File loaded");
            return dt.AsEnumerable().Select(row => row[0].ToString()).ToArray();
        }

        private static void loadContentWiki(string name)
        {
            try
            {
                HtmlWeb web = new HtmlWeb();
                HtmlDocument doc = web.Load(convertUri(name));

                var germanText = doc.DocumentNode.SelectNodes("//span[@lang='de']");
                bool success;
                if (success = germanText?.Count >= 2)
                {
                    HtmlNode cardName = germanText[0]; //Card name
                    HtmlNode cardDesc = germanText[1]; //Card description

                    if (success = (cardDesc != null && cardName != null))
                    {
                        updateCDBFileThroughName(name, getHtmlText(cardName), getHtmlText(cardDesc));
                    }
                }
                if (!success)
                {
                    Console.WriteLine($"Card not found. Name:{name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Website not found. Name{name}, Exception:{ex.Message}");
            }
        }

        private static string convertUri(string name)
        {
            return new Uri(wiki+name.Replace("#",string.Empty)).AbsoluteUri;
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

        private static void updateCDBFileThroughName(string name, string nameGerman, string desc)
        {
            try
            {
                SQLiteCommand cmd = sqlite.CreateCommand();
                cmd.CommandText = $"update texts set name=@german, desc=@desc where name=@name";
                var test =
                cmd.Parameters.Add(new SQLiteParameter { Value = name, ParameterName = "@name", DbType = DbType.String });
                cmd.Parameters.Add(new SQLiteParameter { Value = nameGerman, ParameterName = "@german", DbType = DbType.String });
                cmd.Parameters.Add(new SQLiteParameter { Value = desc, ParameterName = "@desc", DbType = DbType.String });
                cmd.Prepare();
                cmd.ExecuteNonQuery();
                Console.WriteLine($"Card with Name:{name}, {nameGerman} updated");
            }
            catch (SQLiteException ex)
            {
                Console.WriteLine($"Error while updating data: {ex.Message}, name:{name}, german:{nameGerman} desc:{desc}");
            }
        }
    }
}
