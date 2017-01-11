using System;
using System.Net;
using System.IO;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using HtmlAgilityPack;


namespace www.wuxiaworld.com__downloader {
    class Program {
        static void Main(string[] args) {
            // the url and below the chapter number where the system shall start to count
            string url = @"http://www.wuxiaworld.com/desolate-era-index/de-book-18-chapter-1/";
            //string url = @"C:\Users\adria\Documents\Visual Studio 2015\Projects\Projects\www.wuxiaworld.com  downloader\test\DE Book 21, Chapter 35 – Wuxiaworld.html";
            string b_name = "Desolate Era";
            string path = @"C:\Users\adria\Documents\Visual Studio 2015\Projects\Projects\www.wuxiaworld.com  downloader\test\";

            // the starting count number
            const int beg_num = 564;
            int end_num = beg_num;
            Console.WriteLine("online");
            Console.OutputEncoding = Encoding.UTF8;

            // the data storage until the end
            List<Dictionary<string, string>> s_data = new List<Dictionary<string, string>>();

            while (true) {
                // getting the new data
                string data = Download(url);

                Dictionary<string, string> here = new Dictionary<string, string>();
                here = Extract(end_num.ToString(), data);

                if (here["n_url"] != null || here["n_url"] != "" || here["text"] != null || here["title"] != null) {

                    Console.WriteLine($"the chapter: '{here["title"]}' was extracted!");
                   /* Console.WriteLine(here["text"]);
                    Console.WriteLine(here["n_url"]);*/
                    url = here["n_url"];
                    s_data.Add(here);
                    end_num++;
                    //break;
                } else {
                    break;
                }
            }
           
            // writting the code out
            string f_path = $"{path}{b_name} {beg_num} - {end_num}.html";
            WriteFile(f_path, WriteWebpage(s_data));

            Console.WriteLine("offline");
            Console.ReadKey();
        }

        public static string Download(string url) {
            /*
             This method will get the http page from the webserver
             */
            string data;
            if (!url.StartsWith(@"C:\")) {

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                // If required by the server, set the credentials.
                request.Credentials = CredentialCache.DefaultCredentials;
                // set the header responses to gzip or deflate (to save data space)
                request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                // get the decoded and resived request 
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                // change the http responce to a normal string

                using (Stream reader = response.GetResponseStream()) {
                    using (StreamReader s_reader = new StreamReader(reader, Encoding.UTF8)) {
                        data = s_reader.ReadToEnd();
                    }
                }
            } else {
                data = File.ReadAllText(url);
            }

            return data;
        }

        public static Dictionary<string, string> Extract(string c_id, string webpage) {
            /*
             * Returns:
             *      Dictionary<string, string>
             *          'c_id'
             *          'c_title',
             *          'c_text',
             */

            // set's the text to return
            string c_title = null;
            string c_text = null;
            string n_url = null;

            // the dictionary where everything is saved in
            Dictionary<string, string> data = new Dictionary<string, string>();

            // the list to store the html content in
            List<string> t_text = new List<string>() { };

            // create the document
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(webpage);

            // getting the text (with it's nodes) and the title of the chapter
            // getting the content of the text
            HtmlNode content = doc.DocumentNode.SelectSingleNode("//article/div[@class='entry-content']/div/div[@itemprop='articleBody']");

            HtmlNode next_url = content.SelectSingleNode(".//a[text()[contains(.,'Next Chapter')]]");
            n_url = next_url.Attributes["href"].Value;

            if (content != null) {

                // set's the system to catch the p's between the hr
                bool f_hr = false;

                // working
                foreach (HtmlNode text in content.Descendants(1)) {
                    if (text != null) {

                        // filter all the unwanted content (except the p and the hr)
                        if (text.OriginalName == "p" || text.OriginalName == "hr") {
                            // toggle the f_hr flag
                            if (text.OriginalName == "hr") {
                                f_hr = !f_hr;
                            } else if (f_hr == true) {
                                string temp_text = text.InnerText.Trim();
                                // filtering the title
                                if (text.SelectNodes(".//*[self::strong or self::b]") != null && c_title == null){
                                    c_title = temp_text;
                                } else {
                                    t_text.Add($"<p>{temp_text}</p>");
                                }
                            }

                        }

                    } else {
                        // raise an error if there is a problem with the webpage
                        throw new NullReferenceException("There is a problem with the node you selected.");
                    }
                }

            }

            // if there even is some content
            if (t_text.Any()) {
                // joining the text together
                c_text = String.Join("", t_text);
            }
            // sending the data away
            data.Add("c_id", c_id);
            data.Add("title", c_title);
            data.Add("text", c_text);
            data.Add("n_url", n_url);
            return data;
        }

        public static string WriteWebpage(List<Dictionary<string, string>> data) {
            /*
             * The method that will write out all the collected data to a webpage and
             * return the finished webpage.
             * 
             * Variables:
             *      data
             *      
             * Returns:
             *      webpage
             */
            // the returned string
            string webpage;
            const string html_base = "<!doctype html><html lang = \"en\"><head><meta charset = \"utf-8\"/><title></title></head><body></body></html>";
            
            // creating the document
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html_base);
            HtmlNode body = doc.DocumentNode.SelectSingleNode("//body");

            // looping over the data
            for (int i = 0; i< data.Count; i++) {
                // new tags
                HtmlNode div = doc.CreateElement("div");
                HtmlNode title = doc.CreateElement("h1");

                // add the title
                // replace the - through the – 
                title.InnerHtml = Regex.Replace($"Chapter {data[i]["c_id"]}: {data[i]["title"]}", @"(\s?[-–]\s?)", " – ");
                HtmlNode content = doc.CreateTextNode(data[i]["text"]);

                // appending the content
                div.AppendChild(title);
                div.AppendChild(content);

                body.AppendChild(div);

            }

            webpage = doc.DocumentNode.OuterHtml;
            return webpage;
        }

        public static void WriteFile(string path, string data) {
            File.WriteAllText(path, data);
        }
    }
}
