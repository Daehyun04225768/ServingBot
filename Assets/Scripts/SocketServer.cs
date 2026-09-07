using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

/// <summary>
/// v1.1.0 — 싱글 윈도우 멀티 에이전트: 기존에는 로봇마다 별도 exe·별도 포트가 필요했지만,
/// 이제 하나의 포트에서 여러 클라이언트(로봇1, 로봇2)를 동시에 받습니다.
/// 각 클라이언트는 접속 직후 반드시 "Connect:&lt;agentId&gt;" 핸드셰이크를 먼저 보내야 하며,
/// 이후 그 연결에서 오는 모든 명령은 해당 agentId로 라우팅됩니다.
/// (포트로 로봇을 구분하던 -port= 인자 방식은 v1.1.0부터 폐기)
/// </summary>
public class SocketServer : MonoBehaviour
{
    public int port = 5555;

    private TcpListener server;
    private Thread listenerThread;
    private bool isRunning = false;

    private readonly ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();

    void Start()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("-port="))
            {
                int.TryParse(arg.Substring("-port=".Length), out port);
            }
        }

        listenerThread = new Thread(new ThreadStart(StartServer));
        listenerThread.IsBackground = true;
        listenerThread.Start();
    }

    void StartServer()
    {
        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            isRunning = true;
            Debug.Log($"[Unity 서버] 포트 {port} 개방. 접속 대기중... (다중 에이전트 동시 접속 지원)");

            while (isRunning)
            {
                TcpClient newClient = server.AcceptTcpClient();
                Thread clientThread = new Thread(() => HandleClient(newClient));
                clientThread.IsBackground = true;
                clientThread.Start();
            }
        }
        catch (System.Exception e)
        {
            Debug.Log("❌ 서버 에러: " + e.Message);
        }
    }

    void HandleClient(TcpClient client)
    {
        int agentId = -1;
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                string data = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                if (agentId == -1)
                {
                    // 첫 메시지는 반드시 핸드셰이크: "Connect:1" / "Connect:2"
                    if (data.StartsWith("Connect:") && int.TryParse(data.Substring("Connect:".Length), out int parsedId))
                    {
                        agentId = parsedId;
                        clients[agentId] = client;
                        Debug.Log($"✅ agent {agentId} 클라이언트 접속 성공!");
                        ProtocolManager.Instance?.OnClientConnected(agentId);
                    }
                    continue;
                }

                if (ProtocolManager.Instance != null)
                {
                    ProtocolManager.Instance.OnReceiveCommand(agentId, data);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"❌ 클라이언트(agent {agentId}) 통신 에러: {e.Message}");
        }
        finally
        {
            if (agentId != -1) clients.TryRemove(agentId, out _);
            client.Close();
        }
    }

    // ProtocolManager가 호출하여 특정 agentId의 파이썬 클라이언트에게 메시지를 보낼 때 사용
    public void SendMessageToClient(int agentId, string message)
    {
        if (clients.TryGetValue(agentId, out TcpClient client) && client.Connected)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] msg = Encoding.UTF8.GetBytes(message);
                stream.Write(msg, 0, msg.Length);
            }
            catch (System.Exception e)
            {
                Debug.Log($"❌ 송신 에러(agent {agentId}): {e.Message}");
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (server != null) server.Stop();
        if (listenerThread != null) listenerThread.Abort();
    }
}
