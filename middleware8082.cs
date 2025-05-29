using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Form1());
    }
}

public class Form1 : Form
{
    private Button button1;
    private RichTextBox sentTextBox;
    private RichTextBox receivedTextBox;
    private RichTextBox readyTextBox;
    private TcpListener listener;
    private int messageId = 0;

    private static int PORT = 8082;
    private static int[] ports = { 8082, 8083, 8084, 8085, 8086};

    private ConcurrentDictionary<string, List<Command>> concurrentMap = new ConcurrentDictionary<string, List<Command>>();
    public Form1()
    {
        this.Text = "Middleware" + PORT;
        this.Width = 900;
        this.Height = 500;

        button1 = new Button();
        button1.Size = new System.Drawing.Size(100, 30);
        button1.Location = new System.Drawing.Point(0, 15);
        button1.Text = "Send Message";
        button1.Click += new EventHandler(button1_Click);
        Controls.Add(button1);

        Label sentLabel = new Label();
        sentLabel.Text = "Sent";
        sentLabel.Location = new System.Drawing.Point(0, 45);
        sentLabel.AutoSize = true;
        Controls.Add(sentLabel);

        sentTextBox = new RichTextBox();
        sentTextBox.Location = new System.Drawing.Point(0, 60);
        sentTextBox.Width = 300;
        sentTextBox.Height = 400;
        sentTextBox.Multiline = true;
        sentTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        Controls.Add(sentTextBox);

        Label receivedLabel = new Label();
        receivedLabel.Text = "Received";
        receivedLabel.Location = new System.Drawing.Point(300, 45);
        receivedLabel.AutoSize = true;
        Controls.Add(receivedLabel);

        receivedTextBox = new RichTextBox();
        receivedTextBox.Location = new System.Drawing.Point(300, 60);
        receivedTextBox.Width = 300;
        receivedTextBox.Height = 400;
        receivedTextBox.Multiline = true;
        receivedTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        Controls.Add(receivedTextBox);

        Label readyLabel = new Label();
        readyLabel.Text = "Ready";
        readyLabel.Location = new System.Drawing.Point(600, 45);
        readyLabel.AutoSize = true;
        Controls.Add(readyLabel);

        readyTextBox = new RichTextBox();
        readyTextBox.Location = new System.Drawing.Point(600, 60);
        readyTextBox.Width = 600;
        readyTextBox.Height = 400;
        readyTextBox.Multiline = true;
        readyTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        Controls.Add(readyTextBox);

        listener = new TcpListener(IPAddress.Any, PORT);
        listener.Start();
        ListenForClientsAsync();
    }

    private async void ListenForClientsAsync()
    {
        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            ReadMessageAsync(client);
        }
    }

    private async void ReadMessageAsync(TcpClient client)
    {
        byte[] buffer = new byte[1024];
        await client.GetStream().ReadAsync(buffer, 0, buffer.Length);
        string json = Encoding.UTF8.GetString(buffer).Trim('\0');
        Command command = JsonConvert.DeserializeObject<Command>(json);

        if (command.Type.Equals("send"))
        {
            Command cmd = command.Clone();
            cmd.Type = "timestamp";
            cmd.Timestamp = DateTime.Now;
            json = JsonConvert.SerializeObject(cmd);
            receivedTextBox.AppendText($"{json}\n\n");
            SendCommandToSender(cmd);
        }
        else if (command.Type.Equals("timestamp"))
        {
            var key = command.From + "_" + command.Id;
            concurrentMap.AddOrUpdate(key, new List<Command> { command }, (k, v) => { v.Add(command); return v; });            
            if (concurrentMap[key].Count == ports.Length)
            {
                List<Command> commands = concurrentMap[key];
                Command cmd = commands.OrderByDescending(c => c.Timestamp).FirstOrDefault();
                cmd.Type = "maxTimestamp";
                SendCommandToAll(cmd);

                concurrentMap.TryRemove(key, out _);
            }

        }
        else if (command.Type.Equals("maxTimestamp"))
        {
            readyTextBox.AppendText($"{json}\n\n");
        }
    }

    private void SendCommandToSender(Command cmd)
    {
        string json = JsonConvert.SerializeObject(cmd);
        byte[] data = Encoding.UTF8.GetBytes(json);
                
        TcpClient client = new TcpClient("localhost", cmd.From);
        client.GetStream().WriteAsync(data, 0, data.Length);
        //receivedTextBox.AppendText($"-------Send Command to Sender: . {json}\n\n");
        
    }

    private async void SendCommandToAll(Command cmd)
    {
        string json = JsonConvert.SerializeObject(cmd);
        byte[] data = Encoding.UTF8.GetBytes(json);

        for (int i = 0; i < ports.Length; i++)
        {
            TcpClient client = new TcpClient("localhost", ports[i]);
            await client.GetStream().WriteAsync(data, 0, data.Length);
            //receivedTextBox.AppendText($"-------Send Command to port: {ports[i]}. {json}\n\n");
        }
    }

    private async void button1_Click(object sender, EventArgs e)
    {
        Command command = new Command
        {
            From = PORT,
            Id = messageId++,
            Type = "send",
            Message = "from " + PORT,
            Timestamp = DateTime.Now
        };
        string json = JsonConvert.SerializeObject(command);

        TcpClient client = new TcpClient("localhost", 8081);
        byte[] data = Encoding.UTF8.GetBytes(json);
        await client.GetStream().WriteAsync(data, 0, data.Length);

        sentTextBox.AppendText($"{json}\n\n");

    }
}


public class Command
{
    public int From { get; set; }
    public int Id { get; set; }
    public string Type { get; set; }
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }

    public Command Clone()
    {
        string json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<Command>(json);
    }
}

public class CommandKey : IEquatable<CommandKey>
{
    public int From { get; set; }
    public int Id { get; set; }

    public override bool Equals(object obj)
    {
        return Equals(obj as CommandKey);
    }

    public bool Equals(CommandKey other)
    {
        return other != null &&
               From == other.From &&
               Id == other.Id;
    }

    public override int GetHashCode()
    {
        unchecked // ‘ –ÌÀ„ ı“Á≥ˆ
        {
            int hash = 17;
            hash = hash * 23 + From.GetHashCode();
            hash = hash * 23 + Id.GetHashCode();
            return hash;
        }
    }
}