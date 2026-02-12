package com.discordminecraft.bridge;

import de.bluecolored.bluemap.api.BlueMapAPI;
import de.bluecolored.bluemap.api.BlueMapMap;
import de.bluecolored.bluemap.api.markers.MarkerSet;
import de.bluecolored.bluemap.api.markers.POIMarker;

import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * Manages BlueMap marker sets for villages and buildings.
 * Uses BlueMap's Java API to create/update/remove markers when
 * villages and buildings are created or archived.
 */
public final class BlueMapIntegration {

    private static final String VILLAGE_MARKER_SET_ID = "discord-villages";
    private static final String VILLAGE_MARKER_SET_LABEL = "Discord Villages";
    private static final String BUILDING_MARKER_SET_ID = "discord-buildings";
    private static final String BUILDING_MARKER_SET_LABEL = "Discord Buildings";

    private final Logger logger;
    private volatile boolean apiAvailable = false;

    private final Map<String, MarkerData> villageMarkers = new ConcurrentHashMap<>();
    private final Map<String, MarkerData> buildingMarkers = new ConcurrentHashMap<>();

    public BlueMapIntegration(Logger logger) {
        this.logger = logger;
    }

    /**
     * Registers with the BlueMap API lifecycle.
     */
    public void enable() {
        BlueMapAPI.onEnable(api -> {
            apiAvailable = true;
            logger.info("BlueMap API available — registering marker sets");
            restoreMarkers(api);
        });

        BlueMapAPI.onDisable(api -> {
            apiAvailable = false;
            logger.info("BlueMap API disabled");
        });
    }

    public void disable() {
        // Clear API listeners on plugin disable
        apiAvailable = false;
    }

    public boolean isAvailable() {
        return apiAvailable;
    }

    /**
     * Creates or updates a village marker at the given coordinates.
     */
    public void setVillageMarker(String villageId, String label, int centerX, int centerZ) {
        villageMarkers.put(villageId, new MarkerData(label, centerX, 65, centerZ, false));
        applyToApi(VILLAGE_MARKER_SET_ID, VILLAGE_MARKER_SET_LABEL,
                villageId, label, centerX, 65, centerZ);
    }

    /**
     * Creates or updates a building marker at the given coordinates.
     */
    public void setBuildingMarker(String buildingId, String label, int x, int z) {
        buildingMarkers.put(buildingId, new MarkerData(label, x, 65, z, false));
        applyToApi(BUILDING_MARKER_SET_ID, BUILDING_MARKER_SET_LABEL,
                buildingId, label, x, 65, z);
    }

    /**
     * Marks a building as archived — updates the marker label with [Archived] prefix.
     */
    public void archiveBuildingMarker(String buildingId) {
        MarkerData existing = buildingMarkers.get(buildingId);
        if (existing != null) {
            String archivedLabel = "[Archived] " + existing.label;
            buildingMarkers.put(buildingId, new MarkerData(archivedLabel, existing.x, existing.y, existing.z, true));
            applyToApi(BUILDING_MARKER_SET_ID, BUILDING_MARKER_SET_LABEL,
                    buildingId, archivedLabel, existing.x, existing.y, existing.z);
        }
    }

    /**
     * Removes a village marker and all associated building markers.
     */
    public void archiveVillageMarker(String villageId) {
        MarkerData existing = villageMarkers.get(villageId);
        if (existing != null) {
            String archivedLabel = "[Archived] " + existing.label;
            villageMarkers.put(villageId, new MarkerData(archivedLabel, existing.x, existing.y, existing.z, true));
            applyToApi(VILLAGE_MARKER_SET_ID, VILLAGE_MARKER_SET_LABEL,
                    villageId, archivedLabel, existing.x, existing.y, existing.z);
        }
    }

    /**
     * Removes a building marker entirely.
     */
    public void removeBuildingMarker(String buildingId) {
        buildingMarkers.remove(buildingId);
        removeFromApi(BUILDING_MARKER_SET_ID, buildingId);
    }

    private void applyToApi(String markerSetId, String markerSetLabel,
                            String markerId, String label, int x, int y, int z) {
        Optional<BlueMapAPI> apiOpt = BlueMapAPI.getInstance();
        if (apiOpt.isEmpty()) return;

        BlueMapAPI api = apiOpt.get();
        try {
            for (BlueMapMap map : api.getMaps()) {
                if (map.getId().contains("nether") || map.getId().contains("end")) continue;

                MarkerSet markerSet = map.getMarkerSets()
                        .computeIfAbsent(markerSetId, id -> MarkerSet.builder()
                                .label(markerSetLabel)
                                .defaultHidden(false)
                                .build());

                POIMarker marker = POIMarker.builder()
                        .label(label)
                        .position(x, y, z)
                        .build();

                markerSet.getMarkers().put(markerId, marker);
            }
        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to set BlueMap marker: " + markerId, e);
        }
    }

    private void removeFromApi(String markerSetId, String markerId) {
        Optional<BlueMapAPI> apiOpt = BlueMapAPI.getInstance();
        if (apiOpt.isEmpty()) return;

        BlueMapAPI api = apiOpt.get();
        try {
            for (BlueMapMap map : api.getMaps()) {
                MarkerSet markerSet = map.getMarkerSets().get(markerSetId);
                if (markerSet != null) {
                    markerSet.getMarkers().remove(markerId);
                }
            }
        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to remove BlueMap marker: " + markerId, e);
        }
    }

    private void restoreMarkers(BlueMapAPI api) {
        villageMarkers.forEach((id, data) ->
                applyToApi(VILLAGE_MARKER_SET_ID, VILLAGE_MARKER_SET_LABEL,
                        id, data.label, data.x, data.y, data.z));
        buildingMarkers.forEach((id, data) ->
                applyToApi(BUILDING_MARKER_SET_ID, BUILDING_MARKER_SET_LABEL,
                        id, data.label, data.x, data.y, data.z));
        logger.info("Restored " + villageMarkers.size() + " village and "
                + buildingMarkers.size() + " building markers");
    }

    private record MarkerData(String label, int x, int y, int z, boolean archived) {}
}
