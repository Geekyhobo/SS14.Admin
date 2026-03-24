// Logs Grid - Side Panel with API-fetched details

const sidepanel = document.getElementById('side-panel');
const overlay = document.getElementById('side-panel-overlay');
const entries = document.querySelectorAll('.data-log');

entries.forEach(entry => entry.addEventListener('click', onDataGridEntryClick));

overlay.addEventListener('click', closePanel);

/**
 * Close the side panel with animation
 */
function closePanel() {
    sidepanel.classList.add('translate-x-full');
    overlay.classList.add('hidden');

    // Remove transition classes after animation completes
    setTimeout(() => {
        sidepanel.classList.add('hidden-right');
        sidepanel.innerHTML = '';
    }, 300);
}

/**
 * Handle click on a log entry - fetch details from API
 */
async function onDataGridEntryClick(e) {
    const row = e.currentTarget;
    const roundId = row.dataset.roundId;
    const logId = row.dataset.logId;

    if (!roundId || !logId) {
        console.error('Missing roundId or logId');
        return;
    }

    // Show loading state
    showLoadingState();

    try {
        const response = await fetch(`/api/adminlogs/${roundId}/${logId}`);

        if (!response.ok) {
            throw new Error('Failed to fetch log details');
        }

        const logDetails = await response.json();
        renderLogDetails(logDetails);
    } catch (error) {
        console.error('Error fetching log details:', error);
        renderErrorState(error.message);
    }
}

/**
 * Show loading state in the side panel
 */
function showLoadingState() {
    // Show overlay
    overlay.classList.remove('hidden');
    overlay.classList.add('fade-in');

    // Reset and show panel
    sidepanel.classList.remove('hidden-right', 'translate-x-full');
    sidepanel.classList.add('slide-in-from-right');

    // Render loading content
    sidepanel.innerHTML = `
        <div class="flex flex-col h-full" style="padding: 1.5rem;">
            <div class="flex items-center justify-between mb-6 pb-4 border-b" style="border-color: var(--border-light);">
                <h2 class="text-xl font-bold" style="color: var(--ss14-text-primary-light);">
                    Log Details
                </h2>
                <button onclick="closePanel()" class="p-2 rounded-lg transition-colors" style="background: transparent;" onmouseover="this.style.background='var(--ss14-light-bg-3)'" onmouseout="this.style.background='transparent'">
                    <i class="fas fa-times text-xl" style="color: var(--ss14-text-primary-light);"></i>
                </button>
            </div>

            <div class="flex-1 flex items-center justify-center">
                <div class="flex flex-col items-center gap-4">
                    <div class="w-12 h-12 border-4" style="border-color: var(--ss14-red); border-top-color: transparent; border-radius: 50%; animation: spin 1s linear infinite;"></div>
                    <p style="color: var(--ss14-text-secondary-light);">Loading...</p>
                </div>
            </div>
        </div>
    `;
}

/**
 * Render the log details in the side panel
 */
function renderLogDetails(log) {
    const impactColor = getImpactColor(log.impact);
    const impactText = getImpactText(log.impact);
    const formattedDate = new Date(log.date).toLocaleString();
    const jsonContent = formatJson(log.json);
    const playersList = renderPlayers(log.players);

    // Get CSS variable values for current theme
    const textPrimary = "var(--ss14-text-primary-light)";
    const textSecondary = "var(--ss14-text-secondary-light)";
    const bg2 = "var(--ss14-light-bg-2)";
    const bg3 = "var(--ss14-light-bg-3)";
    const borderColor = "var(--border-light)";

    sidepanel.innerHTML = `
        <div class="flex flex-col h-full" style="padding: 1.5rem;">
            <!-- Header -->
            <div class="flex items-center justify-between mb-6 pb-4 border-b-2" style="border-color: ${impactColor}; margin: -1.5rem -1.5rem 1.5rem; padding: 1rem 1.5rem; background-color: ${impactColor}20;">
                <div>
                    <h2 class="text-xl font-bold" style="color: ${impactColor};">
                        Log Details
                    </h2>
                    <span class="text-sm font-medium px-2 py-0.5 rounded" style="background-color: ${impactColor}20; color: ${impactColor};">
                        ${impactText}
                    </span>
                </div>
                <button onclick="closePanel()" class="p-2 rounded-lg transition-all" style="background: transparent;" onmouseover="this.style.background='${bg3}'" onmouseout="this.style.background='transparent'">
                    <i class="fas fa-times text-xl" style="color: ${textPrimary};"></i>
                </button>
            </div>

            <!-- Content -->
            <div class="flex-1 overflow-y-auto space-y-6">
                <!-- Basic Info -->
                <div class="rounded-lg p-4 space-y-3" style="background-color: ${bg2};">
                    <div class="grid grid-cols-[120px_1fr] gap-2">
                        <span class="text-sm font-semibold" style="color: ${textSecondary};">Server:</span>
                        <span style="color: ${textPrimary};">${log.serverName || 'Unknown'}</span>
                    </div>
                    <div class="grid grid-cols-[120px_1fr] gap-2">
                        <span class="text-sm font-semibold" style="color: ${textSecondary};">Round:</span>
                        <span style="color: ${textPrimary};">#${log.roundId}</span>
                    </div>
                    <div class="grid grid-cols-[120px_1fr] gap-2">
                        <span class="text-sm font-semibold" style="color: ${textSecondary};">Log ID:</span>
                        <span style="color: ${textPrimary}; font-family: monospace;">${log.id}</span>
                    </div>
                    <div class="grid grid-cols-[120px_1fr] gap-2">
                        <span class="text-sm font-semibold" style="color: ${textSecondary};">Date:</span>
                        <span style="color: ${textPrimary};">${formattedDate}</span>
                    </div>
                    <div class="grid grid-cols-[120px_1fr] gap-2">
                        <span class="text-sm font-semibold" style="color: ${textSecondary};">Type:</span>
                        <span style="color: ${textPrimary}; font-family: monospace; font-size: 0.875rem;">${log.type}</span>
                    </div>
                </div>

                <!-- Players -->
                ${playersList}

                <!-- Message -->
                <div>
                    <h3 class="text-lg font-semibold mb-2 flex items-center gap-2" style="color: ${textPrimary};">
                        <i class="fas fa-message" style="color: var(--ss14-red);"></i>
                        Message
                    </h3>
                    <div class="rounded-lg p-4" style="background-color: ${bg2};">
                        <p style="color: ${textPrimary}; white-space: pre-wrap; word-break: break-word;">${escapeHtml(log.message)}</p>
                    </div>
                </div>

                <!-- JSON Data -->
                <div>
                    <h3 class="text-lg font-semibold mb-2 flex items-center gap-2" style="color: ${textPrimary};">
                        <i class="fas fa-code" style="color: var(--ss14-red);"></i>
                        Data
                    </h3>
                    <div class="rounded-lg p-4 overflow-x-auto" style="background-color: ${bg2};">
                        <pre style="font-size: 0.875rem; font-family: monospace; color: ${textPrimary}; white-space: pre-wrap;">${jsonContent}</pre>
                    </div>
                </div>
            </div>
        </div>
    `;
}

/**
 * Render players list
 */
function renderPlayers(players) {
    if (!players || players.length === 0) {
        return '';
    }

    const textPrimary = "var(--ss14-text-primary-light)";
    const textSecondary = "var(--ss14-text-secondary-light)";
    const bg2 = "var(--ss14-light-bg-2)";
    const bg3 = "var(--ss14-light-bg-3)";

    const playersHtml = players.map(player => `
        <a href="/players?player=${player.playerUsername}"
           class="flex items-center gap-2 p-2 rounded-lg transition-colors"
           style="text-decoration: none;"
           onmouseover="this.style.background='${bg3}'"
           onmouseout="this.style.background='transparent'">
            <i class="fas fa-user" style="color: ${textSecondary};"></i>
            <span class="font-medium" style="color: var(--ss14-red);">${escapeHtml(player.playerUsername)}</span>
            <span class="text-xs" style="color: ${textSecondary}; font-family: monospace;">${player.playerUserId}</span>
        </a>
    `).join('');

    return `
        <div>
            <h3 class="text-lg font-semibold mb-2 flex items-center gap-2" style="color: ${textPrimary};">
                <i class="fas fa-users" style="color: var(--ss14-red);"></i>
                Players (${players.length})
            </h3>
            <div class="rounded-lg p-2 space-y-1" style="background-color: ${bg2};">
                ${playersHtml}
            </div>
        </div>
    `;
}

/**
 * Render error state
 */
function renderErrorState(message) {
    const textPrimary = "var(--ss14-text-primary-light)";
    const textSecondary = "var(--ss14-text-secondary-light)";

    sidepanel.innerHTML = `
        <div class="flex flex-col h-full" style="padding: 1.5rem;">
            <div class="flex items-center justify-between mb-6 pb-4 border-b" style="border-color: var(--border-light);">
                <h2 class="text-xl font-bold" style="color: ${textPrimary};">
                    Log Details
                </h2>
                <button onclick="closePanel()" class="p-2 rounded-lg transition-colors" style="background: transparent;" onmouseover="this.style.background='var(--ss14-light-bg-3)'" onmouseout="this.style.background='transparent'">
                    <i class="fas fa-times text-xl" style="color: ${textPrimary};"></i>
                </button>
            </div>

            <div class="flex-1 flex items-center justify-center">
                <div class="flex flex-col items-center gap-4 text-center">
                    <div class="w-16 h-16 rounded-full flex items-center justify-center" style="background-color: #fee2e2;">
                        <i class="fas fa-exclamation-triangle text-3xl" style="color: #dc2626;"></i>
                    </div>
                    <div>
                        <p class="text-lg font-semibold" style="color: ${textPrimary};">Error Loading Details</p>
                        <p class="text-sm mt-1" style="color: ${textSecondary};">${escapeHtml(message)}</p>
                    </div>
                    <button onclick="closePanel()" class="px-4 py-2 rounded-lg transition-colors" style="background-color: var(--ss14-red); color: white;">
                        Close
                    </button>
                </div>
            </div>
        </div>
    `;
}

/**
 * Get color for impact level
 */
function getImpactColor(impact) {
    const colors = {
        0: 'var(--color-severity-low)',     // Low
        1: 'var(--color-severity-medium)',  // Medium
        2: 'var(--color-severity-high)',     // High
        3: 'var(--color-severity-extreme)'   // Extreme
    };
    return colors[impact] || 'var(--color-severity-medium)';
}

/**
 * Get text for impact level
 */
function getImpactText(impact) {
    const texts = {
        0: 'Low',
        1: 'Medium',
        2: 'High',
        3: 'Extreme'
    };
    return texts[impact] || 'Unknown';
}

/**
 * Format JSON for display
 */
function formatJson(jsonDoc) {
    try {
        const obj = typeof jsonDoc === 'string' ? JSON.parse(jsonDoc) : jsonDoc;
        return JSON.stringify(obj, null, 2);
    } catch {
        return String(jsonDoc);
    }
}

/**
 * Escape HTML to prevent XSS
 */
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Add spin animation
const style = document.createElement('style');
style.textContent = `
    @keyframes spin {
        from { transform: rotate(0deg); }
        to { transform: rotate(360deg); }
    }
`;
document.head.appendChild(style);

// Make closePanel available globally
window.closePanel = closePanel;
