using Microsoft.AspNetCore.SignalR;
using OhHell.Web.Services;

namespace OhHell.Web.Hubs;

public sealed class GameLobbyHub(MultiplayerGameService multiplayerGameService) : Hub
{
    public async Task<string> CreateRoom(string playerName, int maxPlayers, string boardTheme, string cardTheme, string botDifficulty = "hard")
    {
        var room = multiplayerGameService.CreateRoom(Context.ConnectionId, Context.ConnectionId, playerName, maxPlayers, boardTheme, cardTheme, botDifficulty);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomCode);
        return room.RoomCode;
    }

    public async Task<bool> JoinRoom(string roomCode, string playerName)
    {
        var room = multiplayerGameService.JoinRoom(roomCode, Context.ConnectionId, Context.ConnectionId, playerName);
        if (room is null)
        {
            return false;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomCode);
        return true;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
