using System.Collections.Concurrent;
using OhHell.Web.Models;

namespace OhHell.Web.Services;

public sealed class LobbyRoomService
{
    private readonly ConcurrentDictionary<string, LobbyRoom> rooms = new();

    public LobbyRoom CreateRoom(string connectionId, string playerName)
    {
        var room = new LobbyRoom
        {
            RoomCode = GenerateRoomCode(),
            HostConnectionId = connectionId
        };

        room.Players.Add(playerName);
        rooms[room.RoomCode] = room;
        return room;
    }

    public LobbyRoom? JoinRoom(string roomCode, string playerName)
    {
        if (!rooms.TryGetValue(roomCode.ToUpperInvariant(), out var room))
        {
            return null;
        }

        lock (room.Players)
        {
            if (!room.Players.Contains(playerName))
            {
                room.Players.Add(playerName);
            }
        }

        return room;
    }

    public LobbyRoom? GetRoom(string roomCode)
    {
        rooms.TryGetValue(roomCode.ToUpperInvariant(), out var room);
        return room;
    }

    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = Random.Shared;
        string code;
        do
        {
            code = new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }
        while (rooms.ContainsKey(code));

        return code;
    }
}
