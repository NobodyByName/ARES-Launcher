using Ares.Launcher.Core.Accounts;
using Ares.Launcher.Core.Profiles;

namespace Ares.Launcher.Core.Game;

/// <summary>Plays on a server: signs in, keeps or forgets the saved sign-in, then starts the game with the launch key.</summary>
public sealed class LaunchService(ProfileStore store, SignInClient signIn, GameStarter starter, LauncherPaths paths)
{
    /// <summary>
    /// Signs in to <paramref name="profile"/> as <paramref name="accountName"/>, with the saved sign-in when
    /// <paramref name="password"/> is empty, and starts the game. A rejected saved sign-in is forgotten. After a
    /// successful sign-in the stretched key is saved or forgotten according to <paramref name="remember"/>, the
    /// account name is stored in the profile and <paramref name="signedIn"/> receives it before the game starts.
    /// </summary>
    public async Task PlayAsync(ServerProfile profile, string accountName, string password, bool remember, Action<string>? signedIn = null,
        CancellationToken cancel = default)
    {
        SavedSignIn? saved = store.LoadSignIn(profile);
        SignInResult account;
        try
        {
            account = await signIn.SignInAsync(profile, accountName, password, saved, cancel);
        }
        catch (SignInRejectedException) when (password.Length == 0 && saved is not null)
        {
            store.ForgetSignIn(profile);
            throw;
        }

        if (remember)
        {
            store.SaveSignIn(profile, account.Saved);
        }
        else
        {
            store.ForgetSignIn(profile);
        }

        store.Update(profile with { AccountName = account.Name });
        signedIn?.Invoke(account.Name);
        LauncherSettings settings = LauncherSettings.Load(paths);
        await Task.Run(() => starter.StartAsync(store.Load().Single(q => q.Id == profile.Id), settings, account.LaunchKey, cancel), cancel);
    }
}
