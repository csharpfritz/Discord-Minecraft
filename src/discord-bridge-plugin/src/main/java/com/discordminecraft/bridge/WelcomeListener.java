package com.discordminecraft.bridge;

import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import net.kyori.adventure.text.format.TextDecoration;
import net.kyori.adventure.title.Title;
import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.block.Action;
import org.bukkit.event.player.PlayerInteractEvent;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.entity.Player;

import java.time.Duration;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Handles first-time player welcome experience:
 * - Title screen overlay on join
 * - Actionbar hint about the golden pressure plate
 * - Pressure plate walkthrough explaining the world
 */
public final class WelcomeListener implements Listener {

    private final BridgePlugin plugin;

    // Track players currently in a walkthrough to prevent re-triggering
    private final Set<UUID> activeWalkthroughs = ConcurrentHashMap.newKeySet();

    // Pressure plate location: Crossroads spawn, 8 blocks south of center
    private static final int PLATE_X = 0;
    private static final int PLATE_Y = -59; // BaseY + 1
    private static final int PLATE_Z = 8;

    public WelcomeListener(BridgePlugin plugin) {
        this.plugin = plugin;
    }

    @EventHandler
    public void onPlayerJoin(PlayerJoinEvent event) {
        Player player = event.getPlayer();
        String guildName = plugin.getConfig().getString("guild-name", "Discord World");

        // Title screen overlay
        Title title = Title.title(
                Component.text("Welcome to", NamedTextColor.GOLD)
                        .append(Component.newline())
                        .append(Component.text(guildName, NamedTextColor.YELLOW, TextDecoration.BOLD)),
                Component.text("Your Discord community in Minecraft", NamedTextColor.GRAY),
                Title.Times.times(Duration.ofMillis(500), Duration.ofSeconds(4), Duration.ofMillis(1000))
        );
        player.showTitle(title);

        // Actionbar hint after a short delay
        Bukkit.getScheduler().runTaskLater(plugin, () -> {
            if (player.isOnline()) {
                player.sendActionBar(
                        Component.text("✦ Stand on the golden pressure plate for a tour ✦",
                                NamedTextColor.GOLD, TextDecoration.BOLD));
            }
        }, 100L); // 5 seconds later
    }

    @EventHandler
    public void onPlayerInteract(PlayerInteractEvent event) {
        if (event.getAction() != Action.PHYSICAL) return;
        if (event.getClickedBlock() == null) return;
        if (event.getClickedBlock().getType() != Material.LIGHT_WEIGHTED_PRESSURE_PLATE) return;

        Location loc = event.getClickedBlock().getLocation();
        if (loc.getBlockX() != PLATE_X || loc.getBlockY() != PLATE_Y || loc.getBlockZ() != PLATE_Z) return;

        Player player = event.getPlayer();
        if (activeWalkthroughs.contains(player.getUniqueId())) return;

        activeWalkthroughs.add(player.getUniqueId());
        startWalkthrough(player);
    }

    private void startWalkthrough(Player player) {
        // Step 1: Villages (immediate)
        showStep(player, 0L,
                Component.text("Villages", NamedTextColor.GREEN, TextDecoration.BOLD),
                Component.text("Discord channel categories = Minecraft villages", NamedTextColor.GRAY));

        // Step 2: Buildings (5 seconds)
        showStep(player, 100L,
                Component.text("Buildings", NamedTextColor.AQUA, TextDecoration.BOLD),
                Component.text("Each Discord channel is a building in its village", NamedTextColor.GRAY));

        // Step 3: Minecarts (10 seconds)
        showStep(player, 200L,
                Component.text("Minecart Travel", NamedTextColor.YELLOW, TextDecoration.BOLD),
                Component.text("Rail tracks connect every village to this Crossroads hub", NamedTextColor.GRAY));

        // Step 4: /goto command (15 seconds)
        showStep(player, 300L,
                Component.text("/goto Command", NamedTextColor.LIGHT_PURPLE, TextDecoration.BOLD),
                Component.text("Type /goto <channel> to teleport to any building", NamedTextColor.GRAY));

        // Step 5: Explore (20 seconds) + cleanup
        Bukkit.getScheduler().runTaskLater(plugin, () -> {
            if (player.isOnline()) {
                Title title = Title.title(
                        Component.text("Explore!", NamedTextColor.GOLD, TextDecoration.BOLD),
                        Component.text("Check the lectern nearby for the full world guide", NamedTextColor.GRAY),
                        Title.Times.times(Duration.ofMillis(500), Duration.ofSeconds(4), Duration.ofMillis(1000))
                );
                player.showTitle(title);
            }
            activeWalkthroughs.remove(player.getUniqueId());
        }, 400L);
    }

    private void showStep(Player player, long delayTicks, Component title, Component subtitle) {
        Bukkit.getScheduler().runTaskLater(plugin, () -> {
            if (player.isOnline()) {
                player.showTitle(Title.title(
                        title, subtitle,
                        Title.Times.times(Duration.ofMillis(300), Duration.ofSeconds(4), Duration.ofMillis(500))
                ));
            }
        }, delayTicks);
    }
}
