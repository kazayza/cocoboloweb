(function () {
    'use strict';

    const state = {
        hostId: null,
        host: null,
        svg: null,
        rootGroup: null,
        zoom: null,
        data: null,
        direction: 'top'
    };

    const cfg = {
        viewportWidth: 1100,
        viewportHeight: 660,
        nodeWidth: 170,
        nodeHeight: 108,
        siblingSpacing: 34,
        childrenSpacing: 96,
        margin: { top: 40, right: 40, bottom: 40, left: 40 }
    };

    function getVal(obj, camel, pascal, fallback) {
        if (!obj) return fallback;
        if (obj[camel] !== undefined) return obj[camel];
        if (obj[pascal] !== undefined) return obj[pascal];
        return fallback;
    }

    function normalizeNode(item) {
        return {
            id: getVal(item, 'id', 'Id', ''),
            title: getVal(item, 'title', 'Title', ''),
            countLabel: getVal(item, 'countLabel', 'CountLabel', '0'),
            subLabel: getVal(item, 'subLabel', 'SubLabel', ''),
            bgColor: getVal(item, 'bgColor', 'BgColor', '#ffffff'),
            borderColor: getVal(item, 'borderColor', 'BorderColor', '#cbd5e1'),
            borderHoverColor: getVal(item, 'borderHoverColor', 'BorderHoverColor', '#94a3b8'),
            accentColor: getVal(item, 'accentColor', 'AccentColor', '#475569'),
            children: (getVal(item, 'children', 'Children', []) || []).map(normalizeNode)
        };
    }

    function destroy() {
        if (state.svg) {
            state.svg.remove();
        }
        state.svg = null;
        state.rootGroup = null;
        state.zoom = null;
        if (state.host) {
            state.host.innerHTML = '';
        }
    }

    function getHostSize() {
        const width = Math.max(cfg.viewportWidth, state.host?.clientWidth || cfg.viewportWidth);
        const height = Math.max(cfg.viewportHeight, state.host?.clientHeight || cfg.viewportHeight);
        return { width, height };
    }

    function project(node, size) {
        switch (state.direction) {
            case 'bottom':
                return { x: node.x, y: size.innerHeight - node.y };
            case 'left':
                return { x: node.y, y: node.x };
            case 'right':
                return { x: size.innerWidth - node.y, y: node.x };
            case 'top':
            default:
                return { x: node.x, y: node.y };
        }
    }

    function buildLinkPath(source, target, size) {
        const s = project(source, size);
        const t = project(target, size);

        if (state.direction === 'left' || state.direction === 'right') {
            const midX = (s.x + t.x) / 2;
            return `M${s.x},${s.y} L${midX},${s.y} L${midX},${t.y} L${t.x},${t.y}`;
        }

        const midY = (s.y + t.y) / 2;
        return `M${s.x},${s.y} L${s.x},${midY} L${t.x},${midY} L${t.x},${t.y}`;
    }

    function fitScreen() {
        if (!state.svg || !state.rootGroup || !state.zoom) return;

        const bounds = state.rootGroup.node().getBBox();
        const { width, height } = getHostSize();
        if (!bounds.width || !bounds.height) return;

        const fullWidth = width;
        const fullHeight = height;
        const scale = Math.min(fullWidth / (bounds.width + 80), fullHeight / (bounds.height + 80), 1);
        const tx = fullWidth / 2 - scale * (bounds.x + bounds.width / 2);
        const ty = fullHeight / 2 - scale * (bounds.y + bounds.height / 2);

        state.svg.transition().duration(350).call(
            state.zoom.transform,
            d3.zoomIdentity.translate(tx, ty).scale(scale)
        );
    }

    function renderTree() {
        if (!state.host || !state.data || typeof d3 === 'undefined') return;

        destroy();

        const { width, height } = getHostSize();
        const size = {
            width,
            height,
            innerWidth: width - cfg.margin.left - cfg.margin.right,
            innerHeight: height - cfg.margin.top - cfg.margin.bottom
        };

        state.svg = d3.select(state.host)
            .append('svg')
            .attr('width', width)
            .attr('height', height)
            .attr('viewBox', `0 0 ${width} ${height}`)
            .style('display', 'block')
            .style('background', 'linear-gradient(180deg, #fafaf9, #f8fafc)')
            .style('border', '1px solid #e2e8f0')
            .style('border-radius', '12px');

        state.rootGroup = state.svg.append('g')
            .attr('transform', `translate(${cfg.margin.left},${cfg.margin.top})`);

        state.zoom = d3.zoom()
            .scaleExtent([0.35, 2])
            .on('zoom', (event) => {
                state.rootGroup.attr('transform', event.transform);
            });

        state.svg.call(state.zoom);

        const root = d3.hierarchy(state.data);
        const treeLayout = d3.tree().nodeSize([
            cfg.nodeWidth + cfg.siblingSpacing,
            cfg.nodeHeight + cfg.childrenSpacing
        ]);

        treeLayout(root);

        const links = state.rootGroup.selectAll('.lead-tree-link')
            .data(root.links())
            .enter()
            .append('path')
            .attr('class', 'lead-tree-link')
            .attr('fill', 'none')
            .attr('stroke', '#cbd5e1')
            .attr('stroke-width', 1.2)
            .attr('d', d => buildLinkPath(d.source, d.target, size));

        const nodes = state.rootGroup.selectAll('.lead-tree-node')
            .data(root.descendants())
            .enter()
            .append('g')
            .attr('class', 'lead-tree-node')
            .attr('transform', d => {
                const p = project(d, size);
                return `translate(${p.x - cfg.nodeWidth / 2},${p.y - cfg.nodeHeight / 2})`;
            });

        nodes.append('rect')
            .attr('rx', 14)
            .attr('ry', 14)
            .attr('width', cfg.nodeWidth)
            .attr('height', cfg.nodeHeight)
            .attr('fill', d => d.data.bgColor)
            .attr('stroke', d => d.data.borderColor)
            .attr('stroke-width', 1.4);

        nodes.append('rect')
            .attr('x', 12)
            .attr('y', 10)
            .attr('width', cfg.nodeWidth - 24)
            .attr('height', 6)
            .attr('rx', 4)
            .attr('ry', 4)
            .attr('fill', d => d.data.accentColor);

        nodes.append('text')
            .attr('x', cfg.nodeWidth / 2)
            .attr('y', 40)
            .attr('text-anchor', 'middle')
            .attr('font-size', '13px')
            .attr('font-weight', '800')
            .attr('fill', '#1f2937')
            .text(d => d.data.title || '—');

        nodes.append('text')
            .attr('x', cfg.nodeWidth / 2)
            .attr('y', 67)
            .attr('text-anchor', 'middle')
            .attr('font-size', '24px')
            .attr('font-weight', '900')
            .attr('fill', d => d.data.accentColor)
            .text(d => d.data.countLabel || '0');

        nodes.append('text')
            .attr('x', cfg.nodeWidth / 2)
            .attr('y', 88)
            .attr('text-anchor', 'middle')
            .attr('font-size', '10px')
            .attr('font-weight', '700')
            .attr('fill', '#64748b')
            .text(d => d.data.subLabel || '');

        nodes
            .on('mouseenter', function (_, d) {
                d3.select(this).select('rect')
                    .attr('stroke', d.data.borderHoverColor)
                    .attr('stroke-width', 1.8);
            })
            .on('mouseleave', function (_, d) {
                d3.select(this).select('rect')
                    .attr('stroke', d.data.borderColor)
                    .attr('stroke-width', 1.4);
            });

        requestAnimationFrame(() => fitScreen());
    }

    window.cocoboloLeadJourneyTree = {
        render: function (hostId, data, direction) {
            state.hostId = hostId;
            state.host = document.getElementById(hostId);
            state.data = normalizeNode(data);
            state.direction = direction || state.direction || 'top';
            if (!state.host) return;
            renderTree();
        },
        changeLayout: function (direction) {
            state.direction = direction || 'top';
            if (state.host && state.data) {
                renderTree();
            }
        },
        fit: function () {
            fitScreen();
        },
        destroy: function () {
            destroy();
        }
    };
})();
