using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

/// <summary>
/// Online WallRush-inspired prototype using Photon PUN 2.
/// Add this script to an empty GameObject after importing Photon PUN 2.
/// Each player moves one cell or places a two-cell wall segment per turn.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class WallRushOnlineGame : MonoBehaviourPunCallbacks
{
    [Header("Board")]
    public int boardSize = 9;
    public int wallsPerPlayer = 10;
    public Color blueColor = new Color(0.05f, 0.34f, 0.90f);
    public Color redColor = new Color(0.90f, 0.12f, 0.27f);

    Canvas canvas;
    Font font;
    GameObject root, boardRoot, wallLayer;
    Text statusText, infoText, wallsText, connectionText, chatLog;
    Button wallModeButton, rotateButton;
    Button[,] cells;
    Image[,] cellImages;
    bool[,] blockedRight, blockedUp, wallH, wallV;
    Vector2Int bluePos, redPos;
    int blueWalls, redWalls;
    bool wallMode, horizontalWall = true, gameStarted, gameOver, isBlue, busy;
    int turnActor;
    readonly Queue<string> chatMessages = new Queue<string>();
    string myName;

    static readonly string[] QuickMessages = {
        "Rematch?", "Hi!", "Yes", "No", "Good game!", "😂", "🤔", "🤝", "😡", "👍", "❤️", "👏"
    };

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        myName = "Player" + Random.Range(1000, 9999);
        BuildCanvas();
        ShowLobby();
        PhotonNetwork.AutomaticallySyncScene = false;
        PhotonNetwork.GameVersion = "wallrush-v1";
        PhotonNetwork.ConnectUsingSettings();
    }

    void BuildCanvas()
    {
        var go = new GameObject("WallRush Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(430, 900);
        scaler.matchWidthOrHeight = 0.5f;
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }
    }

    GameObject Panel(string name, Transform parent, Color color, Vector2 amin, Vector2 amax)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return go;
    }

    Text Txt(string value, Transform parent, int size, Color color, TextAnchor align, Vector2 amin, Vector2 amax, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject("Text " + value, typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font = font; t.text = value; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    Button Btn(string label, Transform parent, Color color, Vector2 amin, Vector2 amax, UnityEngine.Events.UnityAction action, int size = 16)
    {
        var go = new GameObject("Button " + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        var b = go.GetComponent<Button>();
        b.onClick.AddListener(action);
        Txt(label, go.transform, size, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, FontStyle.Bold);
        return b;
    }

    void ClearRoot()
    {
        if (root) Destroy(root);
        root = boardRoot = wallLayer = null;
    }

    void ShowLobby()
    {
        ClearRoot();
        root = Panel("Online Lobby", canvas.transform, new Color(0.94f, 0.95f, 0.99f), Vector2.zero, Vector2.one);
        Txt("▦  WallRush Online", root.transform, 32, new Color(0.08f, 0.12f, 0.25f), TextAnchor.MiddleCenter,
            new Vector2(.04f,.79f), new Vector2(.96f,.91f), FontStyle.Bold);
        Txt("BLOCK THEIR WAY", root.transform, 16, new Color(.4f,.46f,.6f), TextAnchor.MiddleCenter,
            new Vector2(.04f,.74f), new Vector2(.96f,.79f), FontStyle.Bold);
        connectionText = Txt("Connecting to online service…", root.transform, 17, new Color(.2f,.25f,.35f),
            TextAnchor.MiddleCenter, new Vector2(.06f,.61f), new Vector2(.94f,.70f));
        Txt("Find a random opponent anywhere online.\nEach player has 10 walls. Reach the opposite row to win.",
            root.transform, 17, new Color(.2f,.24f,.34f), TextAnchor.MiddleCenter,
            new Vector2(.08f,.47f), new Vector2(.92f,.59f));
        Btn("⚡  FIND RANDOM OPPONENT", root.transform, new Color(.02f,.35f,.92f),
            new Vector2(.08f,.34f), new Vector2(.92f,.43f), FindOpponent, 18);
        Btn("Reconnect", root.transform, new Color(.32f,.38f,.5f),
            new Vector2(.20f,.23f), new Vector2(.80f,.30f), Reconnect, 17);
        Txt("Online play needs an internet connection.", root.transform, 13, new Color(.45f,.48f,.57f),
            TextAnchor.MiddleCenter, new Vector2(.05f,.08f), new Vector2(.95f,.16f));
        UpdateConnectionText();
    }

    public override void OnConnectedToMaster()
    {
        UpdateConnectionText();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (connectionText) connectionText.text = "Disconnected: " + cause + ". Tap Reconnect.";
    }

    void Reconnect()
    {
        if (!PhotonNetwork.IsConnected) PhotonNetwork.ConnectUsingSettings();
        else UpdateConnectionText();
    }

    void UpdateConnectionText()
    {
        if (connectionText)
            connectionText.text = PhotonNetwork.IsConnectedAndReady
                ? "🟢 Online — ready to find an opponent"
                : "Connecting… please wait";
    }

    void FindOpponent()
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            if (connectionText) connectionText.text = "Still connecting. Please wait, then tap Find Opponent.";
            return;
        }
        if (connectionText) connectionText.text = "Searching for a random opponent…";
        PhotonNetwork.NickName = myName;
        PhotonNetwork.JoinRandomRoom();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        var options = new RoomOptions { MaxPlayers = 2, IsVisible = true, IsOpen = true };
        PhotonNetwork.CreateRoom(null, options, TypedLobby.Default);
    }

    public override void OnCreatedRoom() { if (connectionText) connectionText.text = "Room created. Waiting for opponent…"; }

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.CurrentRoom.PlayerCount == 2)
            StartOnlineGame();
        else if (connectionText)
            connectionText.text = "Room joined. Waiting for opponent…";
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.CurrentRoom.PlayerCount == 2 && !gameStarted) StartOnlineGame();
    }

    void StartOnlineGame()
    {
        if (gameStarted) return;
        gameStarted = true;
        gameOver = false;
        isBlue = PhotonNetwork.LocalPlayer.ActorNumber < GetOpponentActor();
        turnActor = PhotonNetwork.MasterClient.ActorNumber;
        // Lower actor number is Blue; turn always begins with Blue.
        var blueActor = GetBlueActor();
        turnActor = blueActor;
        ResetBoardData();
        ShowGame();
        UpdateGameUI();
    }

    int GetOpponentActor()
    {
        foreach (Player p in PhotonNetwork.PlayerList)
            if (p.ActorNumber != PhotonNetwork.LocalPlayer.ActorNumber) return p.ActorNumber;
        return PhotonNetwork.LocalPlayer.ActorNumber;
    }

    int GetBlueActor()
    {
        int lowest = int.MaxValue;
        foreach (Player p in PhotonNetwork.PlayerList) if (p.ActorNumber < lowest) lowest = p.ActorNumber;
        return lowest;
    }

    void ResetBoardData()
    {
        blockedRight = new bool[boardSize, boardSize];
        blockedUp = new bool[boardSize, boardSize];
        wallH = new bool[boardSize, boardSize];
        wallV = new bool[boardSize, boardSize];
        bluePos = new Vector2Int(boardSize / 2, boardSize - 1); // top row
        redPos = new Vector2Int(boardSize / 2, 0);             // bottom row
        blueWalls = redWalls = wallsPerPlayer;
        wallMode = false;
        horizontalWall = true;
    }

    void ShowGame()
    {
        ClearRoot();
        root = Panel("Online Match", canvas.transform, new Color(.93f,.95f,.99f), Vector2.zero, Vector2.one);
        Txt("▦ WallRush", root.transform, 26, new Color(.08f,.12f,.25f), TextAnchor.MiddleCenter,
            new Vector2(.03f,.93f), new Vector2(.97f,.99f), FontStyle.Bold);
        Txt("BLUE  " + (isBlue ? "(YOU)" : "") + "             RED  " + (!isBlue ? "(YOU)" : ""),
            root.transform, 14, new Color(.25f,.3f,.4f), TextAnchor.MiddleCenter,
            new Vector2(.03f,.87f), new Vector2(.97f,.92f), FontStyle.Bold);
        statusText = Txt("Starting match…", root.transform, 16, new Color(.1f,.3f,.8f), TextAnchor.MiddleCenter,
            new Vector2(.03f,.82f), new Vector2(.97f,.87f), FontStyle.Bold);
        wallsText = Txt("Walls: Blue 10 • Red 10", root.transform, 14, new Color(.25f,.3f,.4f), TextAnchor.MiddleCenter,
            new Vector2(.03f,.78f), new Vector2(.97f,.82f));
        var frame = Panel("Board Frame", root.transform, new Color(.75f,.8f,.89f), new Vector2(.035f,.27f), new Vector2(.965f,.76f));
        boardRoot = Panel("Board", frame.transform, Color.white, new Vector2(.02f,.02f), new Vector2(.98f,.98f));
        cells = new Button[boardSize,boardSize];
        cellImages = new Image[boardSize,boardSize];
        float cell = 1f / boardSize;
        for (int y=0;y<boardSize;y++) for(int x=0;x<boardSize;x++)
        {
            int cx=x, cy=y;
            var go = new GameObject("Cell "+x+","+y, typeof(Image), typeof(Button));
            go.transform.SetParent(boardRoot.transform,false);
            var rt=go.GetComponent<RectTransform>();
            int row=boardSize-1-y;
            rt.anchorMin=new Vector2(x*cell,row*cell); rt.anchorMax=new Vector2((x+1)*cell,(row+1)*cell);
            rt.offsetMin=new Vector2(1,1); rt.offsetMax=new Vector2(-1,-1);
            var img=go.GetComponent<Image>(); img.color=new Color(.98f,.985f,1f); cellImages[x,y]=img;
            var b=go.GetComponent<Button>(); b.onClick.AddListener(()=>OnCellTap(cx,cy)); cells[x,y]=b;
            Txt("",go.transform,22,Color.white,TextAnchor.MiddleCenter,Vector2.zero,Vector2.one,FontStyle.Bold);
        }
        wallLayer = new GameObject("Walls", typeof(RectTransform));
        wallLayer.transform.SetParent(boardRoot.transform,false);
        var wrt=wallLayer.GetComponent<RectTransform>(); wrt.anchorMin=Vector2.zero; wrt.anchorMax=Vector2.one; wrt.offsetMin=Vector2.zero; wrt.offsetMax=Vector2.zero;

        infoText = Txt("Tap an adjacent cell to move, or use Wall Mode.", root.transform, 12,
            new Color(.35f,.4f,.53f), TextAnchor.MiddleCenter, new Vector2(.03f,.23f), new Vector2(.97f,.27f));
        wallModeButton=Btn("WALL MODE OFF",root.transform,new Color(.22f,.27f,.39f),new Vector2(.04f,.16f),new Vector2(.48f,.22f),ToggleWallMode,13);
        rotateButton=Btn("ROTATE H/V",root.transform,new Color(.14f,.45f,.84f),new Vector2(.51f,.16f),new Vector2(.96f,.22f),ToggleWallDirection,13);
        Txt("QUICK MESSAGES",root.transform,12,new Color(.3f,.35f,.48f),TextAnchor.MiddleCenter,
            new Vector2(.03f,.12f),new Vector2(.97f,.16f),FontStyle.Bold);
        string[] labels={"Rematch?","Hi!","Yes","No","Good game!","😂","🤔","🤝","😡","👍","❤️","👏"};
        for(int i=0;i<labels.Length;i++)
        {
            int idx=i;
            int col=i%4, row=i/4;
            float x0=.035f+col*.235f, y0=.02f+row*.048f;
            Btn(labels[i],root.transform,new Color(.16f,.4f,.88f),new Vector2(x0,y0),new Vector2(x0+.22f,y0+.044f),()=>SendQuickMessage(QuickMessages[idx]),11);
        }
        chatLog = Txt("",root.transform,10,new Color(.22f,.26f,.35f),TextAnchor.MiddleLeft,
            new Vector2(.03f,.065f),new Vector2(.97f,.115f));
    }

    void ToggleWallMode()
    {
        if (!MyTurn() || gameOver) return;
        wallMode=!wallMode;
        var t=wallModeButton.GetComponentInChildren<Text>(); if(t)t.text=wallMode?"WALL MODE ON":"WALL MODE OFF";
        infoText.text=wallMode?"Tap a cell anchor to place a 2-cell "+(horizontalWall?"horizontal":"vertical")+" wall.":"Tap an adjacent cell to move.";
    }

    void ToggleWallDirection()
    {
        horizontalWall=!horizontalWall;
        if(infoText) infoText.text="Wall direction: "+(horizontalWall?"HORIZONTAL":"VERTICAL");
    }

    bool MyTurn() => PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == turnActor;

    void OnCellTap(int x,int y)
    {
        if(gameOver || !MyTurn()) { if(infoText) infoText.text="Wait for your turn."; return; }
        if(wallMode)
        {
            if(!CanPlaceWall(x,y,horizontalWall)) { infoText.text="Invalid wall spot. Try another anchor."; return; }
            photonView.RPC(nameof(RPC_PlaceWall),RpcTarget.All,x,y,horizontalWall,PhotonNetwork.LocalPlayer.ActorNumber);
        }
        else
        {
            Vector2Int from=isBlue?bluePos:redPos, to=new Vector2Int(x,y);
            if(!CanMove(from,to)) { infoText.text="Move one adjacent cell without crossing a wall."; return; }
            photonView.RPC(nameof(RPC_Move),RpcTarget.All,x,y,PhotonNetwork.LocalPlayer.ActorNumber);
        }
    }

    [PunRPC]
    void RPC_Move(int x,int y,int actor)
    {
        if(actor!=turnActor || gameOver) return;
        if(actor==GetBlueActor()) bluePos=new Vector2Int(x,y); else redPos=new Vector2Int(x,y);
        bool won=(actor==GetBlueActor() && y==0) || (actor!=GetBlueActor() && y==boardSize-1);
        if(won) { gameOver=true; statusText.text=(actor==GetBlueActor()?"BLUE":"RED")+" WINS!"; }
        else turnActor=OtherActor(actor);
        wallMode=false; UpdateGameUI();
    }

    [PunRPC]
    void RPC_PlaceWall(int x,int y,bool horizontal,int actor)
    {
        if(actor!=turnActor || gameOver || !CanPlaceWall(x,y,horizontal)) return;
        if(horizontal) { blockedUp[x,y]=true; blockedUp[x+1,y]=true; wallH[x,y]=true; }
        else { blockedRight[x,y]=true; blockedRight[x,y+1]=true; wallV[x,y]=true; }
        if(actor==GetBlueActor()) blueWalls--; else redWalls--;
        turnActor=OtherActor(actor);
        wallMode=false; UpdateGameUI();
    }

    int OtherActor(int actor)
    {
        foreach(Player p in PhotonNetwork.PlayerList) if(p.ActorNumber!=actor)return p.ActorNumber;
        return actor;
    }

    bool CanMove(Vector2Int from,Vector2Int to)
    {
        if(Mathf.Abs(from.x-to.x)+Mathf.Abs(from.y-to.y)!=1 || to.x<0 || to.x>=boardSize || to.y<0 || to.y>=boardSize) return false;
        if(to==bluePos || to==redPos)return false;
        if(to.x==from.x+1)return !blockedRight[from.x,from.y];
        if(to.x==from.x-1)return !blockedRight[to.x,to.y];
        if(to.y==from.y+1)return !blockedUp[from.x,from.y];
        if(to.y==from.y-1)return !blockedUp[to.x,to.y];
        return false;
    }

    bool CanPlaceWall(int x,int y,bool horizontal)
    {
        if((horizontal && x>=boardSize-1)||(!horizontal&&y>=boardSize-1))return false;
        if(horizontal)
        {
            if(blockedUp[x,y]||blockedUp[x+1,y]||wallH[x,y]||(x>0&&wallH[x-1,y])||wallV[x,y]||wallV[x+1,y])return false;
            blockedUp[x,y]=blockedUp[x+1,y]=true;
            bool valid=HasRoute(true)&&HasRoute(false);
            blockedUp[x,y]=blockedUp[x+1,y]=false;
            return valid;
        }
        if(blockedRight[x,y]||blockedRight[x,y+1]||wallV[x,y]||(y>0&&wallV[x,y-1])||wallH[x,y]||wallH[x,y+1])return false;
        blockedRight[x,y]=blockedRight[x,y+1]=true;
        bool ok=HasRoute(true)&&HasRoute(false);
        blockedRight[x,y]=blockedRight[x,y+1]=false;
        return ok;
    }

    bool HasRoute(bool blue)
    {
        var start=blue?bluePos:redPos;
        bool[,] seen=new bool[boardSize,boardSize];
        var q=new Queue<Vector2Int>(); q.Enqueue(start); seen[start.x,start.y]=true;
        Vector2Int[] ds={Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right};
        while(q.Count>0)
        {
            var p=q.Dequeue();
            if((blue&&p.y==0)||(!blue&&p.y==boardSize-1))return true;
            foreach(var d in ds)
            {
                var n=p+d; if(n.x<0||n.x>=boardSize||n.y<0||n.y>=boardSize||seen[n.x,n.y])continue;
                bool open=true;
                if(n.x==p.x+1)open=!blockedRight[p.x,p.y];
                else if(n.x==p.x-1)open=!blockedRight[n.x,n.y];
                else if(n.y==p.y+1)open=!blockedUp[p.x,p.y];
                else if(n.y==p.y-1)open=!blockedUp[n.x,n.y];
                if(open){seen[n.x,n.y]=true;q.Enqueue(n);}
            }
        }
        return false;
    }

    void SendQuickMessage(string message)
    {
        if(!PhotonNetwork.InRoom)return;
        photonView.RPC(nameof(RPC_QuickMessage),RpcTarget.All,PhotonNetwork.LocalPlayer.NickName,message);
    }

    [PunRPC]
    void RPC_QuickMessage(string sender,string message)
    {
        chatMessages.Enqueue(sender+": "+message);
        while(chatMessages.Count>4)chatMessages.Dequeue();
        if(chatLog)chatLog.text=string.Join("\n",chatMessages.ToArray());
        if(infoText)infoText.text=sender+": "+message;
    }

    void UpdateGameUI()
    {
        if(!cellsIsReady())return;
        for(int y=0;y<boardSize;y++)for(int x=0;x<boardSize;x++)
        {
            var img=cellImages[x,y]; var t=img.GetComponentInChildren<Text>();
            if(bluePos.x==x&&bluePos.y==y){img.color=blueColor;if(t){t.text="●";t.color=Color.white;}}
            else if(redPos.x==x&&redPos.y==y){img.color=redColor;if(t){t.text="●";t.color=Color.white;}}
            else {img.color=new Color(.98f,.985f,1f);if(t)t.text="";}
        }
        DrawWalls();
        if(wallsText)wallsText.text="Walls: Blue "+blueWalls+" • Red "+redWalls;
        if(!gameOver && statusText)
        {
            statusText.text=(MyTurn()?"YOUR TURN":"OPPONENT'S TURN")+" • "+(turnActor==GetBlueActor()?"BLUE":"RED");
            statusText.color=turnActor==GetBlueActor()?blueColor:redColor;
        }
    }

    bool cellsIsReady()=>cellImages!=null&&wallLayer!=null;

    void DrawWalls()
    {
        for(int i=wallLayer.transform.childCount-1;i>=0;i--)Destroy(wallLayer.transform.GetChild(i).gameObject);
        float cell=1f/boardSize, thick=cell*.13f;
        for(int y=0;y<boardSize;y++)for(int x=0;x<boardSize;x++)
        {
            if(wallH[x,y])AddWall(new Vector2(x*cell,(boardSize-1-y)*cell-thick/2),new Vector2(2*cell,thick));
            if(wallV[x,y])AddWall(new Vector2((x+1)*cell-thick/2,(boardSize-2-y)*cell),new Vector2(thick,2*cell));
        }
    }

    void AddWall(Vector2 pos,Vector2 size)
    {
        var go=new GameObject("Wall Segment",typeof(Image));go.transform.SetParent(wallLayer.transform,false);
        var rt=go.GetComponent<RectTransform>();rt.anchorMin=pos;rt.anchorMax=pos;rt.pivot=Vector2.zero;
        var parent=wallLayer.GetComponent<RectTransform>();
        rt.sizeDelta=new Vector2(size.x*parent.rect.width,size.y*parent.rect.height);rt.anchoredPosition=Vector2.zero;
        go.GetComponent<Image>().color=new Color(.38f,.42f,.52f);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        gameOver=true;
        if(statusText)statusText.text="OPPONENT LEFT";
        if(infoText)infoText.text="Return to lobby and find another opponent.";
        PhotonNetwork.LeaveRoom();
        gameStarted=false;
    }

    public override void OnLeftRoom()
    {
        gameStarted=false;
        ShowLobby();
    }

    public override void OnCreateRoomFailed(short returnCode,string message)
    {
        if(connectionText)connectionText.text="Could not create room. Try again.";
    }

    public override void OnJoinRoomFailed(short returnCode,string message)
    {
        if(connectionText)connectionText.text="Could not join room. Try again.";
    }
}
