using System;
using System.Net;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using ManyConsole;
using NDesk.Options;
using HtmlAgilityPack;


namespace WuxiaWorldDownloader {

    class Program {
        static string TitleBlackList;
        static int nThreads = 2;

        // the sub thread running thread
        static volatile bool WRun = true;
        // the thread lock
        static Object tLock = new Object();

        static void Main(string[] args) {
            // set the console encoding
            Console.OutputEncoding = Encoding.UTF8;
            // load the commands
            IEnumerable<ConsoleCommand> commands = GetCommands();
            int run = ConsoleCommandDispatcher.DispatchCommand(commands, args, Console.Out);

            if (run == 0) {

                // start the program
                Console.WriteLine("online");

                // download the title blacklist
                // the bool set the source from where to download the file from
                TitleBlackList = GetTitleBlackList(false);
                // the data queue
                ConcurrentQueue<Tuple<int, string>> wQueue = new ConcurrentQueue<Tuple<int, string>>();
                // the work queue
                ConcurrentQueue<Dictionary<string, string>> oQueue = new ConcurrentQueue<Dictionary<string, string>>();
                // add the first url (from the command line)
                wQueue.Enqueue(new Tuple<int, string>(CustomConsoleCommand.BaseNumber, CustomConsoleCommand.BaseUrl));

                // create the threads
                Thread[] Th = new Thread[nThreads];
                for (int i = 0; i < Th.Length; i++) {
                    Th[i] = new Thread(new ThreadStart(() => Work(wQueue, oQueue)));
                }
                // start the threads
                for (int i = 0; i < Th.Length; i++) {
                    Th[i].Start();
                }
                // wait for the worker threads to finish
                for (int i = 0; i < Th.Length; i++) {
                    Th[i].Join();
                }

                // the data storage until the end
                // int = number 
                Dictionary<int, Dictionary<string, string>> s_data = new Dictionary<int, Dictionary<string, string>>();

                // get the extracted data
                while (oQueue.IsEmpty == false) {
                    // create a temporary directory
                    Dictionary<string, string> temp = new Dictionary<string, string>();
                    // get the data
                    bool run_work = oQueue.TryDequeue(out temp);
                    if (run_work) {
                        // send the data to the temporary storage for later processing
                        s_data.Add(Convert.ToInt32(temp["c_id"]), temp);
                    }
                }

                // writting the code out

                string f_path = $"{CustomConsoleCommand.BaseFilePath}\\{CustomConsoleCommand.FileName} {s_data.Keys.Min()} - {s_data.Keys.Max()}.html";
                WriteFile(f_path, WriteWebpage(s_data));

                Console.WriteLine("offline");
                Console.ReadKey();
            }
        }

        public static IEnumerable<ConsoleCommand> GetCommands() {
            // searches for the commands
            return ConsoleCommandDispatcher.FindCommandsInSameAssemblyAs(typeof(Program));
        }

        public static void Work(ConcurrentQueue<Tuple<int, string>> wQueue, ConcurrentQueue<Dictionary<string, string>> oQueue) {
            /*
             * Tuple {
             *  number: url
             * }
             * 
             * wQueue {
             *  
             * }
             * 
             */
            Tuple<int, string> iWork;
            bool tWRun;
            while (true) {
                // check if the kill switch was fliped
                // temporary bool
                lock (tLock) {
                    tWRun = WRun;
                }
                // if the kill switch was set
                if (tWRun) {
                    break;
                }
                // try to get work
                if (wQueue.TryDequeue(out iWork) == false) {
                    // check if there is no work at the moment
                    Thread.Sleep(100);
                } else {
                    // work
                    string tData = Download(iWork.Item2);
                    if (String.IsNullOrWhiteSpace(tData)) {
                        // the kill switch
                        lock (tLock) {
                            WRun = false;
                        }
                        break;
                    } else {
                        Dictionary<string, string> here = Extract(iWork.Item1, tData, wQueue);
                        if (String.IsNullOrWhiteSpace(here["text"]) & String.IsNullOrWhiteSpace(here["title"])) {
                            oQueue.Enqueue(here);
                            Console.WriteLine($"the chapter: '{here["title"]}' was extracted!");
                        }
                    }
                }
            }
        }

        public static string Download(string url) {
            /*
             * This method will get the http page from the webserver
             * 
             * Variables
             *  url
             */
            // the default data is null so that if something happens and the code fails something is returned
            string data = null;

            if (!url.StartsWith(@"C:\")) {
                try {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    // If required by the server, set the credentials.
                    request.Credentials = CredentialCache.DefaultCredentials;
                    // set the header responses to gzip or deflate (to save data space)
                    request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");
                    request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    // get the decoded and resived request 
                    WebResponse w_response = request.GetResponse();
                    HttpWebResponse response = (HttpWebResponse)w_response;
                    // change the http responce to a normal string
                    if (response.StatusCode == HttpStatusCode.OK) {
                        // if the request was successful
                        using (StreamReader s_reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) {
                            data = s_reader.ReadToEnd();
                        }
                    } else if (response.StatusCode == HttpStatusCode.InternalServerError
                        || response.StatusCode == HttpStatusCode.BadRequest
                        || response.StatusCode == HttpStatusCode.BadGateway
                        || response.StatusCode == HttpStatusCode.InternalServerError
                        || response.StatusCode == HttpStatusCode.GatewayTimeout) {
                        // if there was a problem with the server wait a bit and retry
                        Thread.Sleep(100);
                        data = Download(url);
                    } else if (response.StatusCode == HttpStatusCode.Forbidden
                        || response.StatusCode == HttpStatusCode.NotFound) {
                        // if there was a problem with the request data just return nothing
                        data = null;
                    }
                } catch (WebException e) {
                    Console.WriteLine(e.ToString());
                    data = null;
                }

            } else {
                data = File.ReadAllText(url);
            }

            return data;
        }

        public static Dictionary<string, string> Extract(int c_id, string webpage, ConcurrentQueue<Tuple<int, string>> wQueue) {
            /*
             * Returns:
             *      Dictionary<string, string>
             *          'c_id'
             *          'c_title',
             *          'c_text',
             *          //'n_url'
             */

            // set's the text to return
            string c_title = null;
            string c_text = null;
            string n_url = null;

            // the list to store the html content in
            List<string> t_text = new List<string>() { };

            // create the document
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(webpage);

            // getting the text (with it's nodes) and the title of the chapter
            // getting the content of the text
            HtmlNode content = doc.DocumentNode.SelectSingleNode("//article/div[@class='entry-content']/div/div[@itemprop='articleBody']");

            HtmlNode next_url = content.SelectSingleNode(".//a[text()[contains(.,'Next Chapter')]]");

            // send the new url with it's id to the next thread
            n_url = next_url.Attributes["href"].Value;
            wQueue.Enqueue(new Tuple<int, string>(c_id + 1, n_url));

            // continue working
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
                                // filtering for the title
                                if (text.SelectNodes(".//*[self::strong or self::b]") != null && c_title == null) {
                                    // clean up the title 
                                    c_title = Regex.Replace(temp_text, @"(\s?([-–]|&#8211;)\s?)", " – ");
                                    c_title = Regex.Replace(c_title, $"({TitleBlackList})", "");
                                    c_title = Regex.Replace(c_title, @"^\s+?", "");
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
            Dictionary<string, string> data = new Dictionary<string, string>();
            data.Add("c_id", c_id.ToString());
            data.Add("title", c_title);
            data.Add("text", c_text);
            //data.Add("n_url", n_url);
            return data;
        }

        public static string WriteWebpage(Dictionary<int, Dictionary<string, string>> data) {
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
            int[] dataK = data.Keys.ToArray<int>();
            // sort the array from the lowest to the highest
            Array.Sort(dataK);

            for (int i = 0; i < dataK.Length; i++) {
                // new tags
                HtmlNode div = doc.CreateElement("div");
                HtmlNode title = doc.CreateElement("h1");

                // add the title
                // while replacing the - through the – 
                // and checking if the chapter number is already in the title
                if (data[dataK[i]]["title"].Contains($"Chapter {data[dataK[i]]["c_id"]}") == false) {
                    title.InnerHtml = Regex.Replace($"Chapter {data[dataK[i]]["c_id"]}: {data[dataK[i]]["title"]}", @"(\s?[-–]\s?)", " – ");
                } else {
                    title.InnerHtml = Regex.Replace($"{data[dataK[i]]["title"]}", @"(\s?[-–]\s?)", " – ");
                }
                HtmlNode content = doc.CreateTextNode(data[dataK[i]]["text"]);

                // appending the content
                div.AppendChild(title);
                div.AppendChild(content);
                // to the body
                body.AppendChild(div);
            }

            webpage = doc.DocumentNode.OuterHtml;
            return webpage;
        }

        public static void WriteFile(string path, string data) {
            /*
             * The method that will write the data to the system
             */
            File.WriteAllText(path, data);
        }

        public static string GetTitleBlackList(bool online) {
            /*
             * Downloads the blacklisted data from the google drive file and formats it correctly 
             */
            string BlackList;
            string Contents;
            if (online) {

                string url = ("http://drive.google.com/uc?export=download&id=0B2hE6hBecFgzUUxwSG83TUVJbTQ");

                using (WebClient wc = new WebClient()) {
                    Contents = wc.DownloadString(url);
                }
            } else {
                Contents = File.ReadAllText(Directory.GetCurrentDirectory() + "\\TitleBlackList.txt");
            }
            Contents.Trim();
            BlackList = String.Join("|", Contents.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None));
            return BlackList;
        }
    }


    public class CustomConsoleCommand : ConsoleCommand {
        public static string BaseUrl
        {
            get; set;
        }
        public static int BaseNumber
        {
            get; set;
        }
        public static string BaseFilePath
        {
            get; set;
        }
        public static string FileName
        {
            get; set;
        }

        public CustomConsoleCommand() {
            // add the help function
            // Register the actual command with a simple (optional) description.
            this.IsCommand("www.wuxiaworld.com downloader", "This program will download chapters from wuxiaworld.");
            this.SkipsCommandSummaryBeforeRunning();

            this.HasRequiredOption("u|url=", "The base url from that the program will work from.", u => BaseUrl = u);

            this.HasRequiredOption("n|number=", "The base number from where to start counting from.", n => BaseNumber = Convert.ToInt32(n));

            this.HasOption("p|path=", "The base path where to save the end file to.",
                p => BaseFilePath = p);

            // fix the path data
            if (String.IsNullOrWhiteSpace(BaseFilePath)) {
                BaseFilePath = Directory.GetCurrentDirectory();
            }

            this.HasOption("f|file=", "The file name it self (maybe the book name)",
                f => FileName = (String.IsNullOrWhiteSpace(f) ? DateTime.Now.ToString("yyMMdd h:mm") : f));

        }

        public override int Run(string[] remainingArguments) {

            return 0;
        }
    }
}


