<h1 align="center">SonnyTray</h1>
<p align="center"><strong>
<a href="https://github.com/sonny-tel/SonnyTray/releases/latest">Download</a>
</strong></p>

<p align="center">
SonnyTray is a custom Tailscale client for Windows that cleans up and improves the absolutely awful unusable stock client, and adds proper support for things not even supported in the official Tailscale application. All in pretty stylish and cool Fluent design.
</p>

<p align="center">
  <img align="center" height="545" alt="image" src="https://github.com/user-attachments/assets/a85a6c99-0c5e-4787-bf55-a8d65d38dfa3" />
  <img align="center" height="545" alt="image" src="https://github.com/user-attachments/assets/ee7c2291-d057-49b9-8563-59ebbd138b04" />
</p>

# Features

* Complete support for Headscale and custom control servers out of the box
* Location based exit node picker for Mullvad VPN that isn't 5000 nested hover tooltips
* Detailed device view with ping graphing inspired from the feature in Tailscale's android app
* Proper hands free Taildrive support for mounting the WebDAV network drive and creating shares
* All supported Tailscale settings and features that I could think of

# Usage
SonnyTray is not a replacement for tailscaled, it only talks over the IPN. So you will still need to have Tailscale installed, however you don't need to have the stock Tailscale GUI running, if you don't like it you can go to `Settings > Apps > Startup > Tailscale GUI client` disable it, then right click it's icon in the tray menu and click exit.

Get SonnyTray from the [releases](https://github.com/sonny-tel/SonnyTray/releases/latest) page, once it's installed you can open it and enable "Run on startup" in the Settings menu if you want it to always run on boot.
